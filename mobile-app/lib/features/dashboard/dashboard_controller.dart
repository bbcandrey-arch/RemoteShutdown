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

  const DashboardState({
    this.loading = true,
    this.online = false,
    this.errorMessage,
    this.profile,
    this.timers = const [],
    this.lastActionMessage,
    this.pendingActionsCount = 0,
  });

  DashboardState copyWith({
    bool? loading,
    bool? online,
    String? errorMessage,
    DeviceProfile? profile,
    List<TimerTask>? timers,
    String? lastActionMessage,
    int? pendingActionsCount,
  }) {
    return DashboardState(
      loading: loading ?? this.loading,
      online: online ?? this.online,
      errorMessage: errorMessage,
      profile: profile ?? this.profile,
      timers: timers ?? this.timers,
      lastActionMessage: lastActionMessage,
      pendingActionsCount: pendingActionsCount ?? this.pendingActionsCount,
    );
  }
}

/// Владеет подписанным ApiClient для текущего сопряжённого ПК и данными Dashboard
/// (статус/онлайн + активные таймеры, docs/protocol.md §6-§8). Быстрые пресеты
/// отложенного выключения и список активных таймеров живут прямо на Dashboard —
/// так решили в плане архитектуры ради эргономики, вместо отдельного экрана
/// для типового сценария.
class DashboardController extends StateNotifier<DashboardState> {
  final SecureStorageService _secureStorage;
  final DeviceProfileStore _profileStore;
  final PendingActionsQueue _pendingActions;

  ApiClient? _apiClient;
  Uint8List? _sharedSecret;
  bool _flushing = false;

  DashboardController({
    SecureStorageService? secureStorage,
    DeviceProfileStore? profileStore,
    PendingActionsQueue? pendingActions,
  })  : _secureStorage = secureStorage ?? SecureStorageService(),
        _profileStore = profileStore ?? DeviceProfileStore(),
        _pendingActions = pendingActions ?? PendingActionsQueue(),
        super(const DashboardState()) {
    _init();
  }

  Future<void> _init() async {
    final profile = await _profileStore.load();
    final secret = await _secureStorage.readSharedSecret();
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
    final pending = await _pendingActions.load();
    state = state.copyWith(profile: profile, pendingActionsCount: pending.length);
    await refresh();
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
      state = state.copyWith(loading: false, online: false, errorMessage: e.message);
    } catch (e) {
      state = state.copyWith(loading: false, online: false, errorMessage: 'ПК недоступен: $e');
    }
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
      state = state.copyWith(errorMessage: e.message);
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
      state = state.copyWith(errorMessage: e.message);
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
      state = state.copyWith(errorMessage: e.message);
    }
  }

  @override
  void dispose() {
    _apiClient?.close();
    super.dispose();
  }
}

final dashboardControllerProvider = StateNotifierProvider<DashboardController, DashboardState>(
  (ref) => DashboardController(),
);
