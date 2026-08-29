import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../../models/device_profile.dart';

/// Сохраняет (несекретный) список сопряжённых ПК — приложение умеет держать сколько
/// угодно пар одновременно (docs/roadmap.md, "несколько агентов"), не только одну.
/// Сами sharedSecret'ы хранятся отдельно, в SecureStorageService, по одному на
/// clientId — здесь только host/port/имя/отпечаток сертификата на каждый профиль.
///
/// Дополнительно хранит clientId последнего открытого ПК, чтобы при запуске
/// приложение сразу открывало его Dashboard, а не заставляло каждый раз выбирать
/// (см. main.dart, _StartupGate).
class DeviceProfileStore {
  static const _listKey = 'device_profiles_json_v2';
  static const _lastUsedKey = 'last_used_client_id';

  Future<List<DeviceProfile>> loadAll() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getStringList(_listKey) ?? const [];
    final profiles = <DeviceProfile>[];
    for (final entry in raw) {
      try {
        profiles.add(DeviceProfile.fromJson(jsonDecode(entry) as Map<String, dynamic>));
      } catch (_) {
        // Повреждённая запись — пропускаем, не роняем весь список остальных ПК.
      }
    }
    return profiles;
  }

  Future<DeviceProfile?> find(String clientId) async {
    final all = await loadAll();
    for (final p in all) {
      if (p.clientId == clientId) return p;
    }
    return null;
  }

  Future<void> _saveAll(List<DeviceProfile> profiles) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(_listKey, profiles.map((p) => jsonEncode(p.toJson())).toList());
  }

  /// Добавляет новый профиль или обновляет существующий с тем же clientId (например,
  /// при переподключении после отзыва доступа на ПК — clientId там уже новый, так что
  /// на практике это всегда добавление новой записи). Заодно помечает его как
  /// последний использованный.
  Future<void> upsert(DeviceProfile profile) async {
    final all = await loadAll();
    final index = all.indexWhere((p) => p.clientId == profile.clientId);
    if (index >= 0) {
      all[index] = profile;
    } else {
      all.add(profile);
    }
    await _saveAll(all);
    await setLastUsed(profile.clientId);
  }

  /// Локальное переименование ПК в списке (не трогает сам ПК/QR — просто как телефон
  /// его подписывает). См. docs/roadmap.md, "настройка переименования ПК".
  Future<void> rename(String clientId, String newName) async {
    final all = await loadAll();
    final index = all.indexWhere((p) => p.clientId == clientId);
    if (index < 0) return;
    all[index] = all[index].copyWith(deviceName: newName);
    await _saveAll(all);
  }

  /// Удаляет один профиль (разрыв пары с конкретным ПК) — остальные сопряжённые ПК не
  /// затрагиваются. Если удалённый ПК был последним использованным, "последним" вместо
  /// него становится любой из оставшихся (если есть), иначе указатель сбрасывается.
  Future<void> remove(String clientId) async {
    final all = await loadAll();
    all.removeWhere((p) => p.clientId == clientId);
    await _saveAll(all);

    final prefs = await SharedPreferences.getInstance();
    if (prefs.getString(_lastUsedKey) == clientId) {
      if (all.isEmpty) {
        await prefs.remove(_lastUsedKey);
      } else {
        await prefs.setString(_lastUsedKey, all.last.clientId);
      }
    }
  }

  Future<String?> getLastUsedClientId() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(_lastUsedKey);
  }

  Future<void> setLastUsed(String clientId) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_lastUsedKey, clientId);
  }

  /// ПК, который был открыт последним — приложение стартует сразу с его Dashboard, без
  /// экрана выбора, чтобы не тратить время в типовом случае одного ПК (см. main.dart).
  Future<DeviceProfile?> loadLastUsed() async {
    final clientId = await getLastUsedClientId();
    if (clientId == null) return null;
    return find(clientId);
  }
}
