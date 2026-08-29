import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_client.dart';
import '../../core/network/api_exception.dart';
import '../../core/network/tls_pinning_client.dart';
import '../../core/security/device_profile_store.dart';
import '../../core/security/secure_storage_service.dart';
import '../../models/device_profile.dart';
import '../../models/timer_task.dart';

class DashboardState {
  final bool loading;
  final bool online;
  final String? errorMessage;
  final DeviceProfile? profile;
  final List<TimerTask> timers;
  final String? lastActionMessage;

  const DashboardState({
    this.loading = true,
    this.online = false,
    this.errorMessage,
    this.profile,
    this.timers = const [],
    this.lastActionMessage,
  });

  DashboardState copyWith({
    bool? loading,
    bool? online,
    String? errorMessage,
    DeviceProfile? profile,
    List<TimerTask>? timers,
    String? lastActionMessage,
  }) {
    return DashboardState(
      loading: loading ?? this.loading,
      online: online ?? this.online,
      errorMessage: errorMessage,
      profile: profile ?? this.profile,
      timers: timers ?? this.timers,
      lastActionMessage: lastActionMessage,
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

  ApiClient? _apiClient;
  Uint8List? _sharedSecret;

  DashboardController({
    SecureStorageService? secureStorage,
    DeviceProfileStore? profileStore,
  })  : _secureStorage = secureStorage ?? SecureStorageService(),
        _profileStore = profileStore ?? DeviceProfileStore(),
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
    state = state.copyWith(profile: profile);
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
    } on ApiException catch (e) {
      state = state.copyWith(loading: false, online: false, errorMessage: e.message);
    } catch (e) {
      state = state.copyWith(loading: false, online: false, errorMessage: 'ПК недоступен: $e');
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
    }
  }

  Future<void> cancelTimer(String timerId) => _patchTimer(timerId, {'action': 'cancel'});

  Future<void> snoozeTimer(String timerId, int minutes) =>
      _patchTimer(timerId, {'action': 'snooze', 'minutes': minutes});

  Future<void> _patchTimer(String timerId, Map<String, dynamic> body) async {
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
    }
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
