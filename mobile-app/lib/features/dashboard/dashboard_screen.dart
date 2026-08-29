import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../models/timer_task.dart';
import 'dashboard_controller.dart';

/// Главный экран: статус ПК, быстрые команды, пресеты отложенного выключения и список
/// активных таймеров — всё на одном экране, без лишних переходов (эргономика из плана).
class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(dashboardControllerProvider);
    final controller = ref.read(dashboardControllerProvider.notifier);

    return Scaffold(
      appBar: AppBar(
        title: Text(state.profile?.deviceName ?? 'Компьютер'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: controller.refresh,
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: controller.refresh,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            _StatusCard(online: state.online, loading: state.loading, errorMessage: state.errorMessage),
            const SizedBox(height: 16),
            _QuickCommands(controller: controller),
            const SizedBox(height: 16),
            _QuickShutdownPresets(controller: controller),
            const SizedBox(height: 16),
            _VolumeControls(controller: controller),
            const SizedBox(height: 24),
            _ActiveTimersList(timers: state.timers, controller: controller),
          ],
        ),
      ),
    );
  }
}

class _StatusCard extends StatelessWidget {
  final bool online;
  final bool loading;
  final String? errorMessage;

  const _StatusCard({required this.online, required this.loading, required this.errorMessage});

  @override
  Widget build(BuildContext context) {
    final color = online ? Colors.green : Colors.red;
    final label = loading ? 'Проверка…' : (online ? 'ПК онлайн' : 'ПК недоступен');

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Icon(Icons.circle, color: color, size: 14),
            const SizedBox(width: 8),
            Text(label, style: Theme.of(context).textTheme.titleMedium),
            if (loading) ...[
              const SizedBox(width: 12),
              const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)),
            ],
          ],
        ),
      ),
    );
  }
}

class _QuickCommands extends StatelessWidget {
  final DashboardController controller;
  const _QuickCommands({required this.controller});

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        FilledButton.icon(
          onPressed: () => _confirmAndRun(context, 'Выключить ПК сейчас?', controller.shutdownNow),
          icon: const Icon(Icons.power_settings_new),
          label: const Text('Выключить'),
          style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
        ),
        OutlinedButton.icon(
          onPressed: () => _confirmAndRun(context, 'Перезагрузить ПК?', controller.restartNow),
          icon: const Icon(Icons.restart_alt),
          label: const Text('Перезагрузка'),
        ),
        OutlinedButton.icon(
          onPressed: controller.sleepNow,
          icon: const Icon(Icons.bedtime),
          label: const Text('Сон'),
        ),
        OutlinedButton.icon(
          onPressed: controller.hibernateNow,
          icon: const Icon(Icons.ac_unit),
          label: const Text('Гибернация'),
        ),
        OutlinedButton.icon(
          onPressed: controller.lockNow,
          icon: const Icon(Icons.lock),
          label: const Text('Заблокировать'),
        ),
      ],
    );
  }

  void _confirmAndRun(BuildContext context, String message, Future<void> Function() action) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Подтверждение'),
        content: Text(message),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Отмена')),
          FilledButton(
            onPressed: () {
              Navigator.pop(ctx);
              action();
            },
            child: const Text('Да'),
          ),
        ],
      ),
    );
  }
}

/// Ряд быстрых пресетов отложенного выключения прямо на главном экране — одним тапом,
/// без модальных диалогов и перехода на отдельный экран таймера.
class _QuickShutdownPresets extends StatelessWidget {
  final DashboardController controller;
  const _QuickShutdownPresets({required this.controller});

  static const _presets = [15, 30, 60];

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Выключить через…', style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: _presets
              .map((minutes) => ActionChip(
                    label: Text('$minutes мин'),
                    onPressed: () => controller.scheduleShutdownIn(minutes),
                  ))
              .toList(),
        ),
      ],
    );
  }
}

class _VolumeControls extends StatelessWidget {
  final DashboardController controller;
  const _VolumeControls({required this.controller});

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.start,
      children: [
        Text('Громкость', style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(width: 16),
        IconButton(
          icon: const Icon(Icons.volume_down),
          onPressed: () => controller.adjustVolume('down'),
        ),
        IconButton(
          icon: const Icon(Icons.volume_up),
          onPressed: () => controller.adjustVolume('up'),
        ),
        IconButton(
          icon: const Icon(Icons.volume_off),
          onPressed: () => controller.adjustVolume('mute'),
        ),
      ],
    );
  }
}

class _ActiveTimersList extends StatelessWidget {
  final List<TimerTask> timers;
  final DashboardController controller;

  const _ActiveTimersList({required this.timers, required this.controller});

  @override
  Widget build(BuildContext context) {
    if (timers.isEmpty) {
      return const Text('Активных задач нет.');
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Активные задачи', style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: 8),
        ...timers.map((t) => Card(
              child: ListTile(
                leading: const Icon(Icons.timer),
                title: Text(_actionLabel(t.action)),
                subtitle: Text(_formatWhen(t.scheduledAtUtc)),
                trailing: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    IconButton(
                      icon: const Icon(Icons.snooze),
                      tooltip: 'Отложить на 10 мин',
                      onPressed: () => controller.snoozeTimer(t.timerId, 10),
                    ),
                    IconButton(
                      icon: const Icon(Icons.close),
                      tooltip: 'Отменить',
                      onPressed: () => controller.cancelTimer(t.timerId),
                    ),
                  ],
                ),
              ),
            )),
      ],
    );
  }

  String _actionLabel(TimerAction action) => switch (action) {
        TimerAction.shutdown => 'Выключение',
        TimerAction.restart => 'Перезагрузка',
      };

  String _formatWhen(DateTime utc) {
    final local = utc.toLocal();
    final now = DateTime.now();
    final diff = local.difference(now);
    if (diff.inMinutes.abs() < 1) return 'меньше чем через минуту';
    if (diff.inMinutes > 0) return 'через ${diff.inMinutes} мин';
    return 'запланировано на ${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  }
}
