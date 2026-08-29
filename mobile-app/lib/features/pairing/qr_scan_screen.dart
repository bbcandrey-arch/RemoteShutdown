import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

/// Экран сканирования QR-кода сопряжения (docs/roadmap.md, "QR-код пейринг").
/// Windows-агент кодирует в QR host+port и, если PIN уже задан через окно настроек трея,
/// сам PIN (см. windows-agent/.../PairingQrService.cs) — тогда одного скана достаточно для
/// полного сопряжения. Если PIN в QR нет (агент его не знает в открытом виде), пользователь
/// вводит его вручную на следующем экране, как раньше.
class QrScanScreen extends StatefulWidget {
  const QrScanScreen({super.key});

  @override
  State<QrScanScreen> createState() => _QrScanScreenState();
}

class _QrScanScreenState extends State<QrScanScreen> {
  bool _handled = false;

  void _onDetect(BarcodeCapture capture) {
    if (_handled) return;
    final raw = capture.barcodes.firstOrNull?.rawValue;
    if (raw == null) return;

    try {
      final json = jsonDecode(raw) as Map<String, dynamic>;
      final host = json['host'] as String?;
      final port = json['port'] as int?;
      if (host == null || port == null) return;
      final pin = json['pin'] as String?;

      _handled = true;
      Navigator.of(context).pop((host: host, port: port, pin: pin));
    } catch (_) {
      // Не наш QR-код (не JSON с host/port) — просто игнорируем и продолжаем сканировать.
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Сканирование QR-кода')),
      body: Stack(
        children: [
          MobileScanner(onDetect: _onDetect),
          const Positioned(
            left: 0,
            right: 0,
            bottom: 24,
            child: Text(
              'Наведите камеру на QR-код на экране компьютера',
              textAlign: TextAlign.center,
              style: TextStyle(color: Colors.white, fontSize: 16),
            ),
          ),
        ],
      ),
    );
  }
}

extension _FirstOrNull<T> on List<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
