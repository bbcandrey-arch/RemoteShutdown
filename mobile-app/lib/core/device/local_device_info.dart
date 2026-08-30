import 'dart:io' show Platform;

import 'package:device_info_plus/device_info_plus.dart';

/// Платформа + модель ЭТОГО телефона — отправляется при пейринге (`/pair/init`), чтобы
/// на вкладке "Устройства" в трее ПК было видно не просто "Android Phone" для всех
/// подряд, а конкретно какой это телефон (и заранее готово под будущее приложение под
/// iOS — platform различает их). См. docs/roadmap.md.
class LocalDeviceInfo {
  final String platform;
  final String model;

  /// Имя, которое имеет смысл предложить как имя устройства по умолчанию при пейринге
  /// (то, что ПК покажет в списке "Устройства") — конкретная модель информативнее
  /// общей подписи вроде "Android Phone".
  final String displayName;

  const LocalDeviceInfo({required this.platform, required this.model, required this.displayName});

  static Future<LocalDeviceInfo> gather() async {
    final plugin = DeviceInfoPlugin();
    try {
      if (Platform.isAndroid) {
        final info = await plugin.androidInfo;
        final model = [info.manufacturer, info.model].where((s) => s.trim().isNotEmpty).join(' ').trim();
        final display = model.isEmpty ? 'Android' : model;
        return LocalDeviceInfo(platform: 'Android', model: display, displayName: display);
      }
      if (Platform.isIOS) {
        final info = await plugin.iosInfo;
        final display = info.name.isNotEmpty ? info.name : (info.utsname.machine.isNotEmpty ? info.utsname.machine : 'iPhone');
        return LocalDeviceInfo(platform: 'iOS', model: display, displayName: display);
      }
    } catch (_) {
      // Платформенный плагин может не сработать в редких случаях (эмулятор без
      // нужных сервисов и т.п.) — пейринг не должен из-за этого падать целиком.
    }
    return const LocalDeviceInfo(platform: 'Unknown', model: 'Устройство', displayName: 'Устройство');
  }
}
