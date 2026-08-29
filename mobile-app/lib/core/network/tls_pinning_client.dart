import 'dart:io';

import 'package:crypto/crypto.dart';

/// Доверие TLS для самоподписанного сертификата агента (docs/protocol.md §1/§4): здесь
/// нет центра сертификации, поэтому вместо обычной проверки цепочки доверия мы
/// закрепляем (pin) SHA-256 отпечаток сертификата.
///
/// Самое первое подключение к конкретному ПК — trust-on-first-use (TOFU):
/// [expectedFingerprint] возвращает null, любой сертификат принимается, а
/// [onFingerprintObserved] сообщает, что было увидено, чтобы вызывающий код сохранил
/// это по завершении сопряжения. Каждое следующее подключение обязано предъявить точно
/// такой же отпечаток, иначе оно отклоняется — именно это защищает от MITM, который
/// попытается подменить ПК в той же сети после сопряжения.
class TlsPinningClient {
  final String? Function() expectedFingerprint;
  final void Function(String fingerprintHex)? onFingerprintObserved;

  late final HttpClient httpClient;

  TlsPinningClient({required this.expectedFingerprint, this.onFingerprintObserved}) {
    httpClient = HttpClient()
      ..badCertificateCallback = (cert, host, port) {
        final actual = sha256.convert(cert.der).toString().toUpperCase();
        onFingerprintObserved?.call(actual);

        final expected = expectedFingerprint();
        if (expected == null) return true; // TOFU
        return expected.toUpperCase() == actual;
      };
  }

  static String hexOf(List<int> derBytes) => sha256.convert(derBytes).toString().toUpperCase();

  void close() => httpClient.close(force: true);
}
