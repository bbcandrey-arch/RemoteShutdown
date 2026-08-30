import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_client.dart';
import '../../core/network/api_exception.dart';
import '../../core/network/tls_pinning_client.dart';
import '../../core/security/device_profile_store.dart';
import '../../core/security/secure_storage_service.dart';
import '../../core/storage/pending_actions_queue.dart';
import '../../models/device_profile.dart';
import '../../models/timer_task.dart';

class DashboardState {
  final bool loading;
  final bool online;
  final String? errorMessage;
  final DeviceProfile? profile;
  final List<TimerTask> timers;
  final String? lastActionMessage;
  final int pendingActionsCount;

  /// true после того, как сопряжение разорвано (вручную или потому что ПК отозвал
  /// доступ этому устройству, код ответа UNKNOWN_CLIENT) — экран должен вернуться на
  /// PairingScreen. См. DashboardController.unpair().
  final bool unpaired;

  const DashboardState({
    this.loading = true,
    this.online = false,
    this.errorMessage,
    this.profile,
    this.timers = const [],
    this.lastActionMessage,
    this.pendingActionsCount = 0,
    this.unpaired = false,
  });

  DashboardState copyWith({
    bool? loading,
    bool? online,
    String? errorMessage,
    DeviceProfile? profile,
    List<TimerTask>? timers,
    String? lastActionMessage,
    int? pendingActionsCount,
    bool? unpaired,
  }) {
    return DashboardState(
      loading: loading ?? this.loading,
      online: online ?? this.online,
      errorMessage: errorMessage,
      profile: profile ?? this.profile,
      timers: timers ?? this.timers,
      lastActionMessage: lastActionMessage,
      pendingActionsCount: pendingActionsCount ?? this.pendingActionsCount,
      unpaired: unpaired ?? this.unpaired,
    );
  }
}

/// Владеет подписанным ApiClient для ОДНОГО сопряжённого ПК (clientId) и данными его
/// Dashboard (статус/онлайн + активные таймеры, docs/protocol.md §6-§8). Быстрые
/// пресеты отложенного выключения и список активных таймеров живут прямо на
/// Dashboard — так решили в плане архитектуры ради эргономики, вместо отдельного
/// экрана для типового сценария.
///
/// Приложение может быть сопряжено с несколькими ПК одновременно (docs/roadmap.md,
/// "несколько агентов") — на каждый свой DashboardController, см. провайдер ниже
/// (`.family<String>` по clientId).
class DashboardController extends StateNotifier<DashboardState> {
  final String clientId;
  final SecureStorageService _secureStorage;
  final DeviceProfileStore _profileStore;
  final PendingActionsQueue _pendingActions;

  ApiClient? _apiClient;
  Uint8List? _sharedSecret;
  bool _flushing = false;

  DashboardController(
    this.clientId, {
    SecureStorageService? secureStorage,
    DeviceProfileStore? profileStore,
    PendingActionsQueue? pendingActions,
  })  : _secureStorage = secureStorage ?? SecureStorageService(),
        _profileStore = profileStore ?? DeviceProfileStore(),
        _pendingActions = pendingActions ?? PendingActionsQueue(clientId),
        super(const DashboardState()) {
    _init();
  }

  Future<void> _init() async {
    final profile = await _profileStore.find(clientId);
    final secret = await _secureStorage.readSharedSecret(clientId);
    if (profile == null || secret == null) {
      state = state.copyWith(loading: false, errorMessage: 'Нет сопряжённого ПК.');
      return;
    }

    _sharedSecret = secret;
    _apiClient = ApiClient(
      host: profile.host,
      port: profile.port,
      tlsClient: TlsPinningClient(expectedFingerprint: () => profile.certFingerprint),
    );
    // Этот ПК только что открыт — запоминаем как последний использованный, чтобы
    // приложение сразу открывало его при следующем запуске (main.dart, _StartupGate).
    await _profileStore.setLastUsed(clientId);
    final pending = await _pendingActions.load();
    state = state.copyWith(profile: profile, pendingActionsCount: pending.length);
    await refresh();
  }

  /// Локальное переименование этого ПК (docs/roadmap.md, "настройка переименования ПК") —
  /// не отправляется на сам ПК, это только то, как телефон его подписывает.
  Future<void> rename(String newName) async {
    final trimmed = newName.trim();
    if (trimmed.isEmpty) return;
    await _profileStore.rename(clientId, trimmed);
    final profile = await _profileStore.find(clientId);
    state = state.copyWith(profile: profile);
  }

  Future<void> refresh() async {
    if (_apiClient == null || _sharedSecret == null || state.profile == null) return;
    state = state.copyWith(loading: true, errorMessage: null);

    try {
      await _apiClient!.getSigned('/status', clientId: state.profile!.clientId, sharedSecret: _sharedSecret!);
      final timersData =
          await _apiClient!.getSigned('/timers', clientId: state.profile!.clientId, sharedSecret: _sharedSecret!);
      final timers = ((timersData['timers'] as List<dynamic>?) ?? [])
          .map((t) => TimerTask.fromJson(t as Map<String, dynamic>))
          .where((t) => t.status == TimerStatus.pending)
          .toList();

      state = state.copyWith(loading: false, online: true, timers: timers);
      // Связь восстановлена — отправляем накопленные за время офлайна действия
      // (docs/roadmap.md, "Устойчивость к потере соединения").
      await _flushPendingActions();
    } on ApiException catch (e) {
      state = state.copyWith(loading: false, online: false);
      await _handleApiException(e);
    } catch (e) {
      state = state.copyWith(loading: false, online: false, errorMessage: 'ПК недоступен: $e');
    }
  }

  /// Разрывает сопряжение с ЭТИМ ПК на телефоне: чистит его секрет, профиль и
  /// offline-очередь, закрывает клиент — остальные сопряжённые ПК (если есть) не
  /// затрагиваются. Вызывается вручную (кнопка "Отвязать ПК" на Dashboard) или
  /// автоматически, когда ПК больше не узнаёт это устройство (см. _handleApiException) —
  /// баг, который это чинит: раньше отзыв доступа на ПК (вкладка "Устройства" в трее)
  /// не давал телефону узнать об этом, и приложение зависало на Dashboard без
  /// возможности перепривязаться. См. docs/roadmap.md.
  Future<void> unpair() async {
    await _secureStorage.deleteSharedSecret(clientId);
    await _profileStore.remove(clientId);
    await _pendingActions.clear();
    _apiClient?.close();
    _apiClient = null;
    _sharedSecret = null;
    state = state.copyWith(unpaired: true);
  }

  /// Единая точка обработки ошибок сервера: UNKNOWN_CLIENT значит "этот телефон больше
  /// не сопряжён с ПК" (отозван вручную, либо агент переустановлен/сбросил БД) — в этом
  /// случае сразу разрываем локальное сопряжение, а не просто показываем ошибку, на
  /// которую пользователь не может ничего поделать с этого экрана.
  Future<void> _handleApiException(ApiException e) async {
    if (e.code == 'UNKNOWN_CLIENT') {
      await unpair();
      return;
    }
    state = state.copyWith(errorMessage: e.message);
  }

  /// Проигрывает накопленную очередь отложенных действий по порядку, пока сеть
  /// доступна. Останавливается на первой же неудаче (например, связь снова
  /// пропала) — оставшиеся действия останутся в очереди для следующей попытки.
  Future<void> _flushPendingActions() async {
    if (_flushing || _apiClient == null || _sharedSecret == null || state.profile == null) return;
    _flushing = true;
    try {
      var actions = await _pendingActions.load();
      while (actions.isNotEmpty) {
        final action = actions.first;
        try {
          switch (action) {
            case ScheduleShutdownAction(:final minutes):
              await _apiClient!.postSigned(
                '/timers',
                {'action': 'shutdown', 'delaySeconds': minutes * 60},
                clientId: state.profile!.clientId,
                sharedSecret: _sharedSecret!,
              );
            case CancelTimerAction(:final timerId):
              await _apiClient!.patchSigned(
                '/timers/$timerId',
                {'action': 'cancel'},
                clientId: state.profile!.clientId,
                sharedSecret: _sharedSecret!,
              );
            case SnoozeTimerAction(:final timerId, :final minutes):
              await _apiClient!.patchSigned(
                '/timers/$timerId',
                {'action': 'snooze', 'minutes': minutes},
                clientId: state.profile!.clientId,
                sharedSecret: _sharedSecret!,
              );
          }
          await _pendingActions.removeAt(0);
          actions = await _pendingActions.load();
          state = state.copyWith(pendingActionsCount: actions.length);
        } catch (_) {
          // Сеть пропала снова (или сервер отверг запрос) — прекращаем попытку,
          // оставшиеся действия дождутся следующего refresh().
          break;
        }
      }
    } finally {
      _flushing = false;
    }
  }

  Future<void> shutdownNow() => _runCommand('/commands/shutdown', {'delaySeconds': 0}, 'Выключение отправлено');

  Future<void> restartNow() => _runCommand('/commands/restart', {'delaySeconds': 0}, 'Перезагрузка отправлена');

  Future<void> sleepNow() => _runCommand('/commands/sleep', const {}, 'Команда сна отправлена');

  Future<void> hibernateNow() => _runCommand('/commands/hibernate', const {}, 'Команда гибернации отправлена');

  Future<void> lockNow() => _runCommand('/commands/lock', const {}, 'Экран заблокирован');

  Future<void> adjustVolume(String action) =>
      _runCommand('/commands/volume', {'action': action}, null, refreshAfter: false);

  /// Плей/пауза и следующий/предыдущий трек — эмуляция медиаклавиш на ПК
  /// (MediaControlService), не привязано к конкретному плееру. Как и громкость — без
  /// refresh() после и без записи в журнал задач (частое, некритичное действие).
  Future<void> mediaPlayPause() => _runCommand('/commands/media', const {'action': 'playPause'}, null, refreshAfter: false);

  Future<void> mediaNext() => _runCommand('/commands/media', const {'action': 'next'}, null, refreshAfter: false);

  Future<void> mediaPrevious() =>
      _runCommand('/commands/media', const {'action': 'previous'}, null, refreshAfter: false);

  /// Создаёт таймер отложенного выключения (быстрые пресеты на Dashboard: 15/30/60 мин).
  /// При сетевой ошибке (ПК временно недоступен) действие не теряется, а уходит в
  /// [PendingActionsQueue] и будет отправлено автоматически при следующем удачном
  /// подключении — см. docs/roadmap.md, "Устойчивость к потере соединения".
  Future<void> scheduleShutdownIn(int minutes) async {
    if (_apiClient == null || _sharedSecret == null || state.profile == null) return;
    try {
      await _apiClient!.postSigned(
        '/timers',
        {'action': 'shutdown', 'delaySeconds': minutes * 60},
        clientId: state.profile!.clientId,
        sharedSecret: _sharedSecret!,
      );
      state = state.copyWith(lastActionMessage: 'Выключение через $minutes мин запланировано');
      await refresh();
    } on ApiException catch (e) {
      await _handleApiException(e);
    } catch (_) {
      await _enqueue(ScheduleShutdownAction(minutes),
          'Нет связи с ПК — выключение через $minutes мин отправится, когда связь восстановится');
    }
  }

  Future<void> cancelTimer(String timerId) => _patchTimer(
        timerId,
        {'action': 'cancel'},
        fallback: CancelTimerAction(timerId),
        offlineMessage: 'Нет связи с ПК — отмена таймера отправится, когда связь восстановится',
      );

  Future<void> snoozeTimer(String timerId, int minutes) => _patchTimer(
        timerId,
        {'action': 'snooze', 'minutes': minutes},
        fallback: SnoozeTimerAction(timerId, minutes),
        offlineMessage: 'Нет связи с ПК — перенос таймера отправится, когда связь восстановится',
      );

  Future<void> _patchTimer(
    String timerId,
    Map<String, dynamic> body, {
    required PendingAction fallback,
    required String offlineMessage,
  }) async {
    if (_apiClient == null || _sharedSecret == null || state.profile == null) return;
    try {
      await _apiClient!.patchSigned(
        '/timers/$timerId',
        body,
        clientId: state.profile!.clientId,
        sharedSecret: _sharedSecret!,
      );
      await refresh();
    } on ApiException catch (e) {
      await _handleApiException(e);
    } catch (_) {
      await _enqueue(fallback, offlineMessage);
    }
  }

  Future<void> _enqueue(PendingAction action, String message) async {
    await _pendingActions.enqueue(action);
    final actions = await _pendingActions.load();
    state = state.copyWith(lastActionMessage: message, pendingActionsCount: actions.length);
  }

  Future<void> _runCommand(
    String path,
    Map<String, dynamic> body,
    String? successMessage, {
    bool refreshAfter = true,
  }) async {
    if (_apiClient == null || _sharedSecret == null || state.profile == null) return;
    try {
      await _apiClient!.postSigned(path, body, clientId: state.profile!.clientId, sharedSecret: _sharedSecret!);
      state = state.copyWith(lastActionMessage: successMessage);
      if (refreshAfter) await refresh();
    } on ApiException catch (e) {
      await _handleApiException(e);
    }
  }

  @override
  void dispose() {
    _apiClient?.close();
    super.dispose();
  }
}

/// `.family<String>` — по clientId, чтобы каждый сопряжённый ПК держал свой независимый
/// контроллер/состояние (docs/roadmap.md, "несколько агентов"); `.autoDispose` — как и
/// раньше, чтобы при повторном открытии Dashboard того же ПК не подхватывалось протухшее
/// состояние прошлой сессии (см. комментарий у unpair()/_handleApiException()).
final dashboardControllerProvider =
    StateNotifierProvider.autoDispose.family<DashboardController, DashboardState, String>(
  (ref, clientId) => DashboardController(clientId),
);
