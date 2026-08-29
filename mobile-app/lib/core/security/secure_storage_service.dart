import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Хранит секрет сопряжения (docs/protocol.md §4) в защищённом хранилище на базе
/// Android Keystore. Нигде больше его сохранять нельзя (shared_preferences, логи, ...).
class SecureStorageService {
  static const _sharedSecretKey = 'shared_secret_b64';

  final FlutterSecureStorage _storage;

  SecureStorageService({FlutterSecureStorage? storage})
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            );

  Future<void> saveSharedSecret(Uint8List secret) =>
      _storage.write(key: _sharedSecretKey, value: base64Encode(secret));

  Future<Uint8List?> readSharedSecret() async {
    final value = await _storage.read(key: _sharedSecretKey);
    return value == null ? null : base64Decode(value);
  }

  Future<void> clear() => _storage.delete(key: _sharedSecretKey);
}
