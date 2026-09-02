import 'dart:async';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_client.dart';
import '../../core/network/tls_pinning_client.dart';
import '../../core/security/device_profile_store.dart';
import '../../core/security/secure_storage_service.dart';

class TouchpadState {
  final bool ready;
  final String? errorMessage;
  const TouchpadState({this.ready = false, this.errorMessage});
}

/// Лёгкий контроллер тачпада (docs/roadmap.md, "Тачпад") — переиспользует профиль/секрет
/// того же ПК, что и DashboardController, но не тянет статус/таймеры: команды тачпада
/// (движение курсора, клики, текст) идут "выстрелил и забыл" — ответ сервера тут нечего
/// показывать пользователю, а ждать его на каждый жест было бы только лишней задержкой.
class TouchpadController extends StateNotifier<TouchpadState> {
  final String clientId;
  final SecureStorageService _secureStorage;
  final DeviceProfileStore _profileStore;

  ApiClient? _apiClient;
  Uint8List? _sharedSecret;
  String? _deviceClientId;

  TouchpadController(
    this.clientId, {
    SecureStorageService? secureStorage,
    DeviceProfileStore? profileStore,
  })  : _secureStorage = secureStorage ?? SecureStorageService(),
        _profileStore = profileStore ?? DeviceProfileStore(),
        super(const TouchpadState()) {
    _init();
  }

  Future<void> _init() async {
    final profile = await _profileStore.find(clientId);
    final secret = await _secureStorage.readSharedSecret(clientId);
    if (profile == null || secret == null) {
      state = const TouchpadState(errorMessage: 'Нет сопряжённого ПК.');
      return;
    }

    _sharedSecret = secret;
    _deviceClientId = profile.clientId;
    _apiClient = ApiClient(
      host: profile.host,
      port: profile.port,
      tlsClient: TlsPinningClient(expectedFingerprint: () => profile.certFingerprint),
    );
    state = const TouchpadState(ready: true);
  }

  void moveMouse(int dx, int dy) {
    if (dx == 0 && dy == 0) return;
    _fireAndForget('/input/mouse/move', {'dx': dx, 'dy': dy});
  }

  void leftClick() => _fireAndForget('/input/mouse/click', {'button': 'left'});

  void rightClick() => _fireAndForget('/input/mouse/click', {'button': 'right'});

  void typeText(String text) {
    if (text.isEmpty) return;
    _fireAndForget('/input/keyboard/text', {'text': text});
  }

  void sendKey(String key) => _fireAndForget('/input/keyboard/key', {'key': key});

  void _fireAndForget(String path, Map<String, dynamic> body) {
    final api = _apiClient;
    final secret = _sharedSecret;
    final id = _deviceClientId;
    if (api == null || secret == null || id == null) return;
    // Best-effort: одно потерянное движение/клик из-за моргнувшей сети не стоит того,
    // чтобы прерывать жест ошибкой — пользователь просто продолжит вести пальцем.
    unawaited(api.postSigned(path, body, clientId: id, sharedSecret: secret).catchError((_) => const <String, dynamic>{}));
  }

  @override
  void dispose() {
    _apiClient?.close();
    super.dispose();
  }
}

final touchpadControllerProvider =
    StateNotifierProvider.autoDispose.family<TouchpadController, TouchpadState, String>(
  (ref, clientId) => TouchpadController(clientId),
);
