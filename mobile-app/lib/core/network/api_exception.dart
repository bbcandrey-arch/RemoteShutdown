/// Зеркалит конверт ошибки из docs/protocol.md §3/§9.
class ApiException implements Exception {
  final String code;
  final String message;
  final int? statusCode;

  ApiException(this.code, this.message, {this.statusCode});

  @override
  String toString() => 'ApiException($code, $statusCode): $message';
}
