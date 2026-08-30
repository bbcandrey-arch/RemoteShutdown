import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/security/device_profile_store.dart';
import 'features/dashboard/dashboard_screen.dart';
import 'features/pairing/pairing_screen.dart';
import 'theme/app_theme.dart';

void main() {
  runApp(const ProviderScope(child: RemoteShutdownApp()));
}

class RemoteShutdownApp extends StatelessWidget {
  const RemoteShutdownApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Выключатель ПК',
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: ThemeMode.system,
      home: const _StartupGate(),
    );
  }
}

/// При запуске проверяет, есть ли уже сопряжённый ПК: если да — сразу Dashboard,
/// если нет — экран сопряжения.
class _StartupGate extends StatefulWidget {
  const _StartupGate();

  @override
  State<_StartupGate> createState() => _StartupGateState();
}

class _StartupGateState extends State<_StartupGate> {
  bool _loading = true;

  /// clientId ПК, который нужно открыть сразу при запуске — последний использованный
  /// (docs/roadmap.md: "открываться должен всегда последний использовавшийся, чтобы не
  /// тратить время на выбор"), либо null, если сопряжённых ПК ещё нет вовсе.
  String? _clientId;

  @override
  void initState() {
    super.initState();
    _checkExistingProfile();
  }

  Future<void> _checkExistingProfile() async {
    final lastUsed = await DeviceProfileStore().loadLastUsed();
    if (!mounted) return;
    setState(() {
      _clientId = lastUsed?.clientId;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    if (_clientId != null) {
      // При отвязке (вручную или автоматически, см. DashboardController.unpair())
      // DeviceProfileStore уже успел передвинуть "последний использованный" на другой
      // оставшийся ПК, если он есть, — просто перечитываем состояние, а не считаем, что
      // сопряжённых ПК теперь нет вовсе.
      return DashboardScreen(clientId: _clientId!, onUnpaired: _checkExistingProfile);
    }

    return PairingScreen(onPaired: (clientId) => setState(() => _clientId = clientId));
  }
}
