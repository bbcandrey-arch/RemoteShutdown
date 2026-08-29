import 'dart:convert';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';

/// Dart-зеркало HmacSigner.Sign из windows-agent — ОБЯЗАНО строго соответствовать
/// docs/protocol.md §5:
///   signature = HMAC_SHA256(sharedSecret, method + "\n" + path + "\n" + timestamp + "\n" + nonce + "\n" + body)
class HmacSigner {
  static String sign({
    required Uint8List sharedSecret,
    required String method,
    required String path,
    required int timestampMs,
    required String nonce,
    required String body,
  }) {
    final message = '$method\n$path\n$timestampMs\n$nonce\n$body';
    final mac = Hmac(sha256, sharedSecret);
    final digest = mac.convert(utf8.encode(message));
    return base64Encode(digest.bytes);
  }
}
