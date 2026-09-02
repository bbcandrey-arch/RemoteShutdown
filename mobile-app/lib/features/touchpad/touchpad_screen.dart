import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'touchpad_controller.dart';

/// Отдельный экран "Тачпад" (docs/roadmap.md) — курсор мыши и клавиатура ПК прямо с
/// телефона. Переключение между ним и Dashboard — через нижнее меню в обоих экранах
/// (см. DashboardScreen.bottomNavigationBar).
class TouchpadScreen extends ConsumerStatefulWidget {
  final String clientId;
  const TouchpadScreen({super.key, required this.clientId});

  @override
  ConsumerState<TouchpadScreen> createState() => _TouchpadScreenState();
}

class _TouchpadScreenState extends ConsumerState<TouchpadScreen> {
  double _accDx = 0;
  double _accDy = 0;
  Timer? _flushTimer;
  final _textController = TextEditingController();

  @override
  void initState() {
    super.initState();
    // Отправлять на каждый onPanUpdate (может прилетать чаще 60 раз/сек) — слишком
    // часто: копим дельту и сливаем раз в ~30мс одним запросом вместо десятка мелких.
    _flushTimer = Timer.periodic(const Duration(milliseconds: 30), (_) => _flush());
  }

  @override
  void dispose() {
    _flushTimer?.cancel();
    _textController.dispose();
    super.dispose();
  }

  void _flush() {
    if (_accDx == 0 && _accDy == 0) return;
    final dx = _accDx.round();
    final dy = _accDy.round();
    _accDx -= dx;
    _accDy -= dy;
    if (dx == 0 && dy == 0) return;
    ref.read(touchpadControllerProvider(widget.clientId).notifier).moveMouse(dx, dy);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    // ВАЖНО: ref.watch (не read) — держит autoDispose-провайдер живым, пока этот
    // экран в дереве виджетов. read() не подписывается ни на что, и без единого
    // подписчика autoDispose уничтожал контроллер сразу после создания, ещё до того,
    // как его асинхронный _init() успевал завершиться (см. Timer._flush() ниже).
    ref.watch(touchpadControllerProvider(widget.clientId));
    final controller = ref.read(touchpadControllerProvider(widget.clientId).notifier);

    return Scaffold(
      appBar: AppBar(title: const Text('Тачпад')),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            children: [
              Expanded(
                child: GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onPanUpdate: (details) {
                    _accDx += details.delta.dx;
                    _accDy += details.delta.dy;
                  },
                  onTap: controller.leftClick,
                  onLongPress: controller.rightClick,
                  child: Container(
                    width: double.infinity,
                    decoration: BoxDecoration(
                      color: scheme.surfaceContainerHighest,
                      borderRadius: BorderRadius.circular(20),
                    ),
                    alignment: Alignment.center,
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.touch_app, size: 40, color: scheme.onSurfaceVariant),
                        const SizedBox(height: 8),
                        Text(
                          'Ведите пальцем — курсор\nТап — ЛКМ, долгое нажатие — ПКМ',
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: scheme.onSurfaceVariant),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: FilledButton.tonal(
                      onPressed: controller.leftClick,
                      style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 16)),
                      child: const Text('ЛКМ'),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: FilledButton.tonal(
                      onPressed: controller.rightClick,
                      style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 16)),
                      child: const Text('ПКМ'),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _textController,
                      decoration: const InputDecoration(hintText: 'Печатать на ПК…'),
                      textInputAction: TextInputAction.send,
                      onSubmitted: (text) {
                        controller.typeText(text);
                        _textController.clear();
                      },
                    ),
                  ),
                  const SizedBox(width: 4),
                  IconButton.filledTonal(
                    icon: const Icon(Icons.send),
                    onPressed: () {
                      controller.typeText(_textController.text);
                      _textController.clear();
                    },
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Wrap(
                alignment: WrapAlignment.center,
                spacing: 8,
                runSpacing: 8,
                children: [
                  ActionChip(
                    avatar: const Icon(Icons.keyboard_return, size: 18),
                    label: const Text('Enter'),
                    onPressed: () => controller.sendKey('enter'),
                  ),
                  ActionChip(
                    avatar: const Icon(Icons.backspace_outlined, size: 18),
                    label: const Text('Backspace'),
                    onPressed: () => controller.sendKey('backspace'),
                  ),
                  ActionChip(label: const Text('Esc'), onPressed: () => controller.sendKey('escape')),
                  ActionChip(label: const Text('Tab'), onPressed: () => controller.sendKey('tab')),
                  ActionChip(label: const Text('Space'), onPressed: () => controller.sendKey('space')),
                  ActionChip(label: const Text('Del'), onPressed: () => controller.sendKey('delete')),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  IconButton.filledTonal(icon: const Icon(Icons.arrow_back), onPressed: () => controller.sendKey('arrowLeft')),
                  const SizedBox(width: 4),
                  Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      IconButton.filledTonal(icon: const Icon(Icons.arrow_upward), onPressed: () => controller.sendKey('arrowUp')),
                      const SizedBox(height: 4),
                      IconButton.filledTonal(icon: const Icon(Icons.arrow_downward), onPressed: () => controller.sendKey('arrowDown')),
                    ],
                  ),
                  const SizedBox(width: 4),
                  IconButton.filledTonal(icon: const Icon(Icons.arrow_forward), onPressed: () => controller.sendKey('arrowRight')),
                ],
              ),
            ],
          ),
        ),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: 1,
        onDestinationSelected: (index) {
          if (index == 0) Navigator.of(context).pop();
        },
        destinations: const [
          NavigationDestination(icon: Icon(Icons.home_outlined), selectedIcon: Icon(Icons.home), label: 'Управление'),
          NavigationDestination(icon: Icon(Icons.touch_app_outlined), selectedIcon: Icon(Icons.touch_app), label: 'Тачпад'),
        ],
      ),
    );
  }
}
