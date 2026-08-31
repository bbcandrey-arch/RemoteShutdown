import 'dart:io' show Platform;

import 'package:device_info_plus/device_info_plus.dart';

/// Платформа + модель ЭТОГО телефона — отправляется при пейринге (`/pair/init`), чтобы
/// на вкладке "Устройства" в трее ПК было видно не просто "Android Phone" для всех
/// подряд, а конкретно какой это телефон (и заранее готово под будущее приложение под
/// iOS — platform различает их). См. docs/roadmap.md.
class LocalDeviceInfo {
  final String platform;

  /// Полная техническая модель (с производителем, если он не совпадает с моделью) —
  /// для колонки "Модель" в трее. Отдельно от [displayName], чтобы обе колонки не
  /// дублировали друг друга один в один.
  final String model;

  /// Имя, которое имеет смысл предложить как имя устройства по умолчанию при пейринге
  /// (то, что ПК покажет в списке "Устройства", колонка "Устройство") — короче, чем
  /// [model], пользователь всё равно может переименовать в приложении.
  final String displayName;

  const LocalDeviceInfo({required this.platform, required this.model, required this.displayName});

  static Future<LocalDeviceInfo> gather() async {
    final plugin = DeviceInfoPlugin();
    try {
      if (Platform.isAndroid) {
        final info = await plugin.androidInfo;
        final rawModel = info.model.trim();
        final fullModel =
            [info.manufacturer, info.model].where((s) => s.trim().isNotEmpty).join(' ').trim();
        final display = rawModel.isNotEmpty ? rawModel : (fullModel.isEmpty ? 'Android' : fullModel);
        return LocalDeviceInfo(
          platform: 'Android',
          model: fullModel.isNotEmpty ? fullModel : display,
          displayName: display,
        );
      }
      if (Platform.isIOS) {
        final info = await plugin.iosInfo;
        final machine = info.utsname.machine.trim();
        final display = info.name.trim().isNotEmpty ? info.name.trim() : (machine.isNotEmpty ? machine : 'iPhone');
        return LocalDeviceInfo(platform: 'iOS', model: machine.isNotEmpty ? machine : display, displayName: display);
      }
    } catch (_) {
      // Платформенный плагин может не сработать в редких случаях (эмулятор без
      // нужных сервисов и т.п.) — пейринг не должен из-за этого падать целиком.
    }
    return const LocalDeviceInfo(platform: 'Unknown', model: 'Устройство', displayName: 'Устройство');
  }
}
