import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../../models/device_profile.dart';

/// Сохраняет (несекретный) DeviceProfile — последний известный host/port, закреплённый
/// отпечаток сертификата, MAC для WoL — между перезапусками приложения. Сам sharedSecret
/// хранится отдельно, в SecureStorageService.
class DeviceProfileStore {
  static const _key = 'device_profile_json';

  Future<void> save(DeviceProfile profile) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_key, jsonEncode(profile.toJson()));
  }

  Future<DeviceProfile?> load() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_key);
    if (raw == null) return null;
    try {
      return DeviceProfile.fromJson(jsonDecode(raw) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  Future<void> clear() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_key);
  }
}
