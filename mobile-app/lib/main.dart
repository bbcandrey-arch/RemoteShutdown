import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/security/device_profile_store.dart';
import 'features/dashboard/dashboard_screen.dart';
import 'features/pairing/pairing_screen.dart';

void main() {
  runApp(const ProviderScope(child: RemoteShutdownApp()));
}

class RemoteShutdownApp extends StatelessWidget {
  const RemoteShutdownApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Выключение ПК',
      theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: Colors.indigo)),
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
  bool _hasProfile = false;

  @override
  void initState() {
    super.initState();
    _checkExistingProfile();
  }

  Future<void> _checkExistingProfile() async {
    final profile = await DeviceProfileStore().load();
    if (!mounted) return;
    setState(() {
      _hasProfile = profile != null;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    if (_hasProfile) {
      return DashboardScreen(onUnpaired: () => setState(() => _hasProfile = false));
    }

    return PairingScreen(onPaired: () => setState(() => _hasProfile = true));
  }
}
