import 'dart:math';

/// Лёгкий генератор UUID v4 *в форме* RFC-4122 — достаточно для requestId/nonce
/// (docs/protocol.md §5), где нужна лишь "практическая уникальность", а не настоящая
/// UUID-библиотека.
class IdGenerator {
  static final Random _random = Random.secure();

  static String newId() {
    final bytes = List<int>.generate(16, (_) => _random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0F) | 0x40; // версия 4
    bytes[8] = (bytes[8] & 0x3F) | 0x80; // вариант 10xx

    String hex(int start, int end) =>
        bytes.sublist(start, end).map((b) => b.toRadixString(16).padLeft(2, '0')).join();

    return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
  }
}
