/// Всё, что приложение знает о сопряжённом ПК после успешного pairing
/// (docs/protocol.md §4). `sharedSecret` здесь намеренно НЕТ — он хранится только в
/// SecureStorageService, а не в обычной модели, которая может попасть в логи/prefs.
class DeviceProfile {
  final String clientId;
  final String host;
  final int port;
  final String? deviceMac;
  final String? broadcastHint;
  final String deviceName;

  /// SHA-256 отпечаток TLS-сертификата агента, закреплённый при сопряжении
  /// (docs/protocol.md §4, TlsPinningClient). Не секрет — можно хранить вместе
  /// с остальным несекретным профилем в shared_preferences.
  final String certFingerprint;

  const DeviceProfile({
    required this.clientId,
    required this.host,
    required this.port,
    required this.deviceName,
    required this.certFingerprint,
    this.deviceMac,
    this.broadcastHint,
  });

  DeviceProfile copyWith({String? host, int? port, String? deviceMac, String? broadcastHint}) {
    return DeviceProfile(
      clientId: clientId,
      host: host ?? this.host,
      port: port ?? this.port,
      deviceName: deviceName,
      certFingerprint: certFingerprint,
      deviceMac: deviceMac ?? this.deviceMac,
      broadcastHint: broadcastHint ?? this.broadcastHint,
    );
  }

  Map<String, dynamic> toJson() => {
        'clientId': clientId,
        'host': host,
        'port': port,
        'deviceName': deviceName,
        'certFingerprint': certFingerprint,
        'deviceMac': deviceMac,
        'broadcastHint': broadcastHint,
      };

  factory DeviceProfile.fromJson(Map<String, dynamic> json) => DeviceProfile(
        clientId: json['clientId'] as String,
        host: json['host'] as String,
        port: json['port'] as int,
        deviceName: json['deviceName'] as String? ?? 'PC',
        certFingerprint: json['certFingerprint'] as String,
        deviceMac: json['deviceMac'] as String?,
        broadcastHint: json['broadcastHint'] as String?,
      );
}
