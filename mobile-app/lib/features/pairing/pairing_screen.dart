import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'pairing_controller.dart';

/// MVP-экран сопряжения: ручной ввод IP + PIN (docs/protocol.md §4). QR-сопряжение и
/// mDNS/UDP discovery появятся позже (Этап 3 в плане архитектуры) — этот экран в любом
/// случае останется как запасной путь, поэтому он нужен независимо от них.
class PairingScreen extends ConsumerStatefulWidget {
  final VoidCallback onPaired;

  const PairingScreen({super.key, required this.onPaired});

  @override
  ConsumerState<PairingScreen> createState() => _PairingScreenState();
}

class _PairingScreenState extends ConsumerState<PairingScreen> {
  final _hostController = TextEditingController();
  final _portController = TextEditingController(text: '54321');
  final _pinController = TextEditingController();

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
      if (next is PairingSuccess) widget.onPaired();
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
          deviceName: 'Android Phone',
        );
  }

  void _submitPin() {
    final pin = _pinController.text.trim();
    if (pin.isEmpty) return;
    ref.read(pairingControllerProvider.notifier).confirmPin(pin);
  }
}

class _ConnectForm extends StatelessWidget {
  final TextEditingController hostController;
  final TextEditingController portController;
  final String? errorMessage;
  final VoidCallback onSubmit;

  const _ConnectForm({
    required this.hostController,
    required this.portController,
    required this.errorMessage,
    required this.onSubmit,
  });

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text('Введите IP-адрес и порт компьютера в вашей Wi-Fi сети.'),
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
