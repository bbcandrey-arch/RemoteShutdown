import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Хранит секреты сопряжения (docs/protocol.md §4) в защищённом хранилище на базе
/// Android Keystore. Нигде больше их сохранять нельзя (shared_preferences, логи, ...).
///
/// Один секрет на clientId — приложение может быть сопряжено с несколькими ПК
/// одновременно (docs/roadmap.md, "несколько агентов"), у каждого свой sharedSecret.
class SecureStorageService {
  static const _keyPrefix = 'shared_secret_';

  final FlutterSecureStorage _storage;

  SecureStorageService({FlutterSecureStorage? storage})
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            );

  String _keyFor(String clientId) => '$_keyPrefix$clientId';

  Future<void> saveSharedSecret(String clientId, Uint8List secret) =>
      _storage.write(key: _keyFor(clientId), value: base64Encode(secret));

  Future<Uint8List?> readSharedSecret(String clientId) async {
    final value = await _storage.read(key: _keyFor(clientId));
    return value == null ? null : base64Decode(value);
  }

  Future<void> deleteSharedSecret(String clientId) => _storage.delete(key: _keyFor(clientId));
}
