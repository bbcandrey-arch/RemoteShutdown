import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'pairing_controller.dart';
import 'qr_scan_screen.dart';

/// MVP-экран сопряжения: ручной ввод IP + PIN (docs/protocol.md §4). QR-сопряжение и
/// mDNS/UDP discovery появятся позже (Этап 3 в плане архитектуры) — этот экран в любом
/// случае останется как запасной путь, поэтому он нужен независимо от них.
class PairingScreen extends ConsumerStatefulWidget {
  /// Вызывается с clientId только что сопряжённого ПК — используется и при первом
  /// запуске (main.dart, _StartupGate), и при добавлении ещё одного ПК к уже
  /// сопряжённым (см. PcListScreen) — тогда вызывающая сторона решает, что делать:
  /// открыть Dashboard нового ПК или просто вернуться в список.
  final ValueChanged<String> onPaired;

  const PairingScreen({super.key, required this.onPaired});

  @override
  ConsumerState<PairingScreen> createState() => _PairingScreenState();
}

class _PairingScreenState extends ConsumerState<PairingScreen> {
  final _hostController = TextEditingController();
  final _portController = TextEditingController(text: '54321');
  final _pinController = TextEditingController();

  /// PIN, пришедший вместе с host+port из QR-кода (если агент знает его в открытом виде,
  /// см. windows-agent/.../SettingsForm.cs) — используется, чтобы не просто подставить его
  /// в поле, а сразу подтвердить пейринг одним сканом, без ручного ввода.
  String? _scannedPin;

  /// Имя ПК из QR-кода (PairingQrService.PairingQrPayload.deviceName), если оно там было —
  /// передаётся в startPairing как предварительное имя (см. PairingController).
  String? _scannedDeviceName;

  @override
  void dispose() {
    _hostController.dispose();
    _portController.dispose();
    _pinController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(pairingControllerProvider);

    ref.listen<PairingState>(pairingControllerProvider, (previous, next) {
      if (next is PairingSuccess) widget.onPaired(next.profile.clientId);

      // Если PIN пришёл из QR — подтверждаем автоматически, как только контроллер дошёл
      // до экрана ввода PIN, вместо того чтобы заставлять пользователя нажимать ещё раз.
      if (next is PairingAwaitingPin && _scannedPin != null) {
        final pin = _scannedPin!;
        _scannedPin = null;
        _pinController.text = pin;
        ref.read(pairingControllerProvider.notifier).confirmPin(pin);
      }
    });

    return Scaffold(
      appBar: AppBar(title: const Text('Подключение к ПК')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: switch (state) {
          PairingIdle() || PairingFailed() => _ConnectForm(
              hostController: _hostController,
              portController: _portController,
              errorMessage: state is PairingFailed ? state.message : null,
              onSubmit: _submitConnect,
              onScanQr: _scanQr,
            ),
          PairingConnecting() => const _CenteredProgress(label: 'Подключение…'),
          PairingAwaitingPin() => _PinForm(
              pinController: _pinController,
              onSubmit: _submitPin,
              onCancel: () => ref.read(pairingControllerProvider.notifier).reset(),
            ),
          PairingConfirming() => const _CenteredProgress(label: 'Проверка PIN…'),
          PairingSuccess() => const _CenteredProgress(label: 'Готово!'),
        },
      ),
    );
  }

  void _submitConnect() {
    final host = _hostController.text.trim();
    final port = int.tryParse(_portController.text.trim()) ?? 54321;
    if (host.isEmpty) return;
    ref.read(pairingControllerProvider.notifier).startPairing(
          host: host,
          port: port,
          qrDeviceName: _scannedDeviceName,
        );
  }

  void _submitPin() {
    final pin = _pinController.text.trim();
    if (pin.isEmpty) return;
    ref.read(pairingControllerProvider.notifier).confirmPin(pin);
  }

  /// Открывает сканер QR-кода: host+port подставляются в форму; если в QR был и PIN —
  /// запоминаем его и сразу запускаем пейринг (см. ref.listen выше) — сканирование
  /// одного кадра заменяет весь ручной ввод (см. docs/roadmap.md).
  Future<void> _scanQr() async {
    final result = await Navigator.of(context).push<({String host, int port, String? pin, String? deviceName})>(
      MaterialPageRoute(builder: (_) => const QrScanScreen()),
    );
    if (result == null) return;
    _hostController.text = result.host;
    _portController.text = result.port.toString();
    _scannedDeviceName = result.deviceName;

    if (result.pin != null) {
      _scannedPin = result.pin;
      _submitConnect();
    }
  }
}

class _ConnectForm extends StatelessWidget {
  final TextEditingController hostController;
  final TextEditingController portController;
  final String? errorMessage;
  final VoidCallback onSubmit;
  final VoidCallback onScanQr;

  const _ConnectForm({
    required this.hostController,
    required this.portController,
    required this.errorMessage,
    required this.onSubmit,
    required this.onScanQr,
  });

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text('Отсканируйте QR-код на экране компьютера или введите IP и порт вручную.'),
        const SizedBox(height: 16),
        OutlinedButton.icon(
          onPressed: onScanQr,
          icon: const Icon(Icons.qr_code_scanner),
          label: const Text('Сканировать QR-код'),
        ),
        const SizedBox(height: 16),
        TextField(
          controller: hostController,
          decoration: const InputDecoration(labelText: 'IP-адрес ПК', hintText: '192.168.1.42'),
          keyboardType: TextInputType.text,
        ),
        const SizedBox(height: 8),
        TextField(
          controller: portController,
          decoration: const InputDecoration(labelText: 'Порт'),
          keyboardType: TextInputType.number,
        ),
        if (errorMessage != null) ...[
          const SizedBox(height: 12),
          Text(errorMessage!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
        ],
        const SizedBox(height: 24),
        FilledButton(onPressed: onSubmit, child: const Text('Подключиться')),
      ],
    );
  }
}

class _PinForm extends StatelessWidget {
  final TextEditingController pinController;
  final VoidCallback onSubmit;
  final VoidCallback onCancel;

  const _PinForm({required this.pinController, required this.onSubmit, required this.onCancel});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text('Введите PIN, заданный на компьютере.'),
        const SizedBox(height: 16),
        TextField(
          controller: pinController,
          decoration: const InputDecoration(labelText: 'PIN'),
          keyboardType: TextInputType.number,
          obscureText: true,
          autofocus: true,
        ),
        const SizedBox(height: 24),
        FilledButton(onPressed: onSubmit, child: const Text('Подтвердить')),
        TextButton(onPressed: onCancel, child: const Text('Отмена')),
      ],
    );
  }
}

class _CenteredProgress extends StatelessWidget {
  final String label;
  const _CenteredProgress({required this.label});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const CircularProgressIndicator(),
          const SizedBox(height: 16),
          Text(label),
        ],
      ),
    );
  }
}
