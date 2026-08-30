import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/device/local_device_info.dart';
import '../../core/network/api_client.dart';
import '../../core/network/api_exception.dart';
import '../../core/network/tls_pinning_client.dart';
import '../../core/security/device_profile_store.dart';
import '../../core/security/secure_storage_service.dart';
import '../../models/device_profile.dart';

sealed class PairingState {}

class PairingIdle extends PairingState {}

class PairingConnecting extends PairingState {}

/// pair/init прошёл успешно; TLS для этой попытки был доверен по схеме
/// trust-on-first-use (см. TlsPinningClient), и [observedFingerprint] — то, что было
/// закреплено. Телефон теперь ждёт, пока пользователь считает PIN с ПК и введёт его.
class PairingAwaitingPin extends PairingState {
  final String host;
  final int port;
  final String pairingSessionId;
  final String observedFingerprint;

  /// Имя ПК (не телефона!) — из QR-кода, если он его нёс, иначе из ответа /pair/init
  /// (см. PairingController.startPairing). Показывается в приложении как имя этого
  /// сопряжения (заголовок Dashboard, список ПК) — docs/roadmap.md.
  final String pcName;

  PairingAwaitingPin({
    required this.host,
    required this.port,
    required this.pairingSessionId,
    required this.observedFingerprint,
    required this.pcName,
  });
}

class PairingConfirming extends PairingState {}

class PairingSuccess extends PairingState {
  final DeviceProfile profile;
  PairingSuccess(this.profile);
}

class PairingFailed extends PairingState {
  final String message;
  PairingFailed(this.message);
}

/// Реализует флоу сопряжения из docs/protocol.md §4. Как устроено доверие TLS —
/// см. TlsPinningClient (TOFU на pair/init, дальше — закреплено и строго проверяется,
/// начиная с pair/confirm).
class PairingController extends StateNotifier<PairingState> {
  final SecureStorageService _secureStorage;
  final DeviceProfileStore _profileStore;

  PairingController({
    SecureStorageService? secureStorage,
    DeviceProfileStore? profileStore,
  })  : _secureStorage = secureStorage ?? SecureStorageService(),
        _profileStore = profileStore ?? DeviceProfileStore(),
        super(PairingIdle());

  /// [qrDeviceName] — имя ПК из отсканированного QR-кода (PairingQrService.PairingQrPayload
  /// на стороне агента), если пейринг начался со скана — используется как предварительное
  /// имя, пока не пришёл ответ /pair/init (тот и остаётся источником истины, см. ниже).
  Future<void> startPairing({
    required String host,
    required int port,
    String? qrDeviceName,
  }) async {
    state = PairingConnecting();

    String? observedFingerprint;
    final tlsClient = TlsPinningClient(
      expectedFingerprint: () => null, // TOFU: для этого ПК ещё ничего не закреплено.
      onFingerprintObserved: (fp) => observedFingerprint = fp,
    );
    final apiClient = ApiClient(host: host, port: port, tlsClient: tlsClient);

    try {
      // 'deviceName' здесь — имя ЭТОГО телефона, как оно будет видно в списке
      // сопряжённых устройств на ПК (вкладка "Устройства" в трее); имя самого ПК
      // приходит отдельно, полем agentName в ответе ниже. platform/model — отдельные
      // структурированные поля для тех же колонок (см. LocalDeviceInfo).
      final localDevice = await LocalDeviceInfo.gather();
      final data = await apiClient.postUnsigned('/pair/init', {
        'deviceName': localDevice.displayName,
        'platform': localDevice.platform,
        'model': localDevice.model,
      });
      final sessionId = data['pairingSessionId'] as String;
      final pcName = (data['agentName'] as String?) ?? qrDeviceName ?? host;

      if (observedFingerprint == null) {
        // Не должно происходить с самоподписанным сертификатом (badCertificateCallback
        // срабатывает всегда), но лучше отказать, чем продолжить без закреплённого отпечатка.
        state = PairingFailed('Не удалось получить сертификат ПК.');
        return;
      }

      state = PairingAwaitingPin(
        host: host,
        port: port,
        pairingSessionId: sessionId,
        observedFingerprint: observedFingerprint!,
        pcName: pcName,
      );
    } on ApiException catch (e) {
      state = PairingFailed('Не удалось подключиться: ${e.message}');
    } catch (e) {
      state = PairingFailed('Не удалось подключиться: $e');
    } finally {
      apiClient.close();
    }
  }

  Future<void> confirmPin(String pin) async {
    final current = state;
    if (current is! PairingAwaitingPin) return;

    state = PairingConfirming();

    // С этого момента отпечаток, увиденный при pair/init, проверяется строго —
    // если он изменится посреди сопряжения (например, MITM подменил сертификат),
    // соединение будет отклонено, а не молча доверено заново.
    final tlsClient = TlsPinningClient(expectedFingerprint: () => current.observedFingerprint);
    final apiClient = ApiClient(host: current.host, port: current.port, tlsClient: tlsClient);

    try {
      final data = await apiClient.postUnsigned('/pair/confirm', {
        'pairingSessionId': current.pairingSessionId,
        'pin': pin,
      });

      final clientId = data['clientId'] as String;
      final sharedSecretB64 = data['sharedSecret'] as String;
      final deviceMac = data['deviceMac'] as String?;
      final broadcastHint = data['broadcastHint'] as String?;

      final profile = DeviceProfile(
        clientId: clientId,
        host: current.host,
        port: current.port,
        deviceName: current.pcName,
        certFingerprint: current.observedFingerprint,
        deviceMac: deviceMac,
        broadcastHint: broadcastHint,
      );

      await _secureStorage.saveSharedSecret(clientId, Uint8List.fromList(base64Decode(sharedSecretB64)));
      // upsert (не save) — сохраняет этот ПК как ещё один профиль, не стирая уже
      // сопряжённые (docs/roadmap.md, "несколько агентов"), и сразу помечает его
      // последним использованным.
      await _profileStore.upsert(profile);

      state = PairingSuccess(profile);
    } on ApiException catch (e) {
      state = PairingFailed(_friendlyPairError(e));
    } catch (e) {
      state = PairingFailed('Ошибка сопряжения: $e');
    } finally {
      apiClient.close();
    }
  }

  String _friendlyPairError(ApiException e) {
    switch (e.code) {
      case 'INVALID_PIN':
        return 'Неверный PIN.';
      case 'PIN_LOCKED':
        return e.message; // already has the retry-after wording
      case 'SESSION_EXPIRED':
        return 'Сессия сопряжения истекла, попробуйте снова.';
      default:
        // текст ошибки от сервера уже на английском/техническом языке — выводим как есть
        return e.message;
    }
  }

  void reset() => state = PairingIdle();
}

final pairingControllerProvider = StateNotifierProvider.autoDispose<PairingController, PairingState>(
  (ref) => PairingController(),
);
