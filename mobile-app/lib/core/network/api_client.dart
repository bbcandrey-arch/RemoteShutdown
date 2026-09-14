import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';

import 'api_exception.dart';
import 'id_generator.dart';
import 'tls_pinning_client.dart';
import '../security/hmac_signer.dart';

/// Общается с одним Windows Agent по HTTPS, по протоколу docs/protocol.md.
///
/// Два вида запросов:
///  - неподписанные (pair/init, pair/confirm) — sharedSecret ещё не существует,
///    их защищает только проверка закреплённого TLS-сертификата (§4).
///  - подписанные — все остальные эндпоинты, с аутентификацией через HMAC-заголовки
///    из §5.
class ApiClient {
  final String host;
  final int port;
  final TlsPinningClient tlsClient;
  late final http.Client _http;

  ApiClient({required this.host, required this.port, required this.tlsClient}) {
    _http = IOClient(tlsClient.httpClient);
  }

  Uri _uri(String path) => Uri.https('$host:$port', path);

  void close() {
    _http.close();
    tlsClient.close();
  }

  Future<Map<String, dynamic>> postUnsigned(String path, Map<String, dynamic> body) async {
    final response = await _http.post(
      _uri(path),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode(body),
    );
    return _parse(response);
  }

  Future<Map<String, dynamic>> getSigned(
    String path, {
    required String clientId,
    required Uint8List sharedSecret,
  }) =>
      _sendSigned('GET', path, null, clientId: clientId, sharedSecret: sharedSecret);

  Future<Map<String, dynamic>> postSigned(
    String path,
    Map<String, dynamic> body, {
    required String clientId,
    required Uint8List sharedSecret,
  }) =>
      _sendSigned('POST', path, body, clientId: clientId, sharedSecret: sharedSecret);

  Future<Map<String, dynamic>> patchSigned(
    String path,
    Map<String, dynamic> body, {
    required String clientId,
    required Uint8List sharedSecret,
  }) =>
      _sendSigned('PATCH', path, body, clientId: clientId, sharedSecret: sharedSecret);

  Future<Map<String, dynamic>> _sendSigned(
    String method,
    String path,
    Map<String, dynamic>? body, {
    required String clientId,
    required Uint8List sharedSecret,
  }) async {
    final bodyStr = body == null ? '' : jsonEncode(body);
    final timestamp = DateTime.now().toUtc().millisecondsSinceEpoch;
    final nonce = IdGenerator.newId();
    final requestId = IdGenerator.newId();

    final signature = HmacSigner.sign(
      sharedSecret: sharedSecret,
      method: method,
      path: path,
      timestampMs: timestamp,
      nonce: nonce,
      body: bodyStr,
    );

    final headers = {
      'Content-Type': 'application/json',
      'X-Request-Id': requestId,
      'X-Client-Id': clientId,
      'X-Timestamp': '$timestamp',
      'X-Nonce': nonce,
      'X-Signature': signature,
    };

    final uri = _uri(path);
    late http.Response response;
    switch (method) {
      case 'GET':
        response = await _http.get(uri, headers: headers);
        break;
      case 'POST':
        response = await _http.post(uri, headers: headers, body: bodyStr);
        break;
      case 'PATCH':
        response = await _http.patch(uri, headers: headers, body: bodyStr);
        break;
      default:
        throw ArgumentError('Unsupported method $method');
    }
    return _parse(response);
  }

  Map<String, dynamic> _parse(http.Response response) {
    final Map<String, dynamic> json;
    try {
      json = jsonDecode(response.body) as Map<String, dynamic>;
    } catch (_) {
      throw ApiException('INTERNAL_ERROR', 'Malformed response (HTTP ${response.statusCode})',
          statusCode: response.statusCode);
    }

    // Сервер теперь всегда отдаёт camelCase (см. windows-agent Envelope.cs, ApiJson.Options —
    // раньше Results.Json(...) на ответах об ошибке сериализовал PascalCase в отличие от
    // Results.Ok(...), и КАЖДАЯ ошибка сервера тонула тут в generic "Unknown error", а
    // настоящий код вроде UNKNOWN_CLIENT/STALE_REQUEST никогда не доходил до пользователя —
    // баг-репорт "ПК недоступен" без внятной причины). Разбираем оба варианта регистра
    // как защиту про запас, а не только потому, что сейчас это строго обязательно.
    final status = json['status'] ?? json['Status'];
    if (status != 'ok') {
      final error = (json['error'] ?? json['Error']) as Map<String, dynamic>?;
      throw ApiException(
        (error?['code'] ?? error?['Code']) as String? ?? 'INTERNAL_ERROR',
        (error?['message'] ?? error?['Message']) as String? ?? 'Unknown error',
        statusCode: response.statusCode,
      );
    }
    return ((json['data'] ?? json['Data']) as Map<String, dynamic>?) ?? const {};
  }
}
