import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_info.dart';
import '../../models/timer_task.dart';
import '../../theme/app_theme.dart';
import '../pc_list/pc_list_screen.dart';
import '../touchpad/touchpad_screen.dart';
import 'dashboard_controller.dart';

/// Главный экран: статус ПК, быстрые команды, пресеты отложенного выключения и список
/// активных таймеров — всё на одном экране, без лишних переходов (эргономика из плана).
///
/// Один экземпляр отвечает за один сопряжённый ПК ([clientId]) — при нескольких
/// сопряжённых ПК (docs/roadmap.md, "несколько агентов") переключение между ними идёт
/// через [PcListScreen] (иконка в AppBar).
class DashboardScreen extends ConsumerWidget {
  final String clientId;
  final VoidCallback onUnpaired;

  const DashboardScreen({super.key, required this.clientId, required this.onUnpaired});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final provider = dashboardControllerProvider(clientId);
    final state = ref.watch(provider);
    final controller = ref.read(provider.notifier);

    // Сопряжение разорвано (вручную или ПК отозвал доступ этому устройству, см.
    // DashboardController.unpair()/_handleApiException) — уходим на экран сопряжения.
    ref.listen<DashboardState>(provider, (previous, next) {
      if (next.unpaired) onUnpaired();
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(state.profile?.deviceName ?? 'Компьютер'),
        // Material 3: в AppBar — только самые частые действия (обновить, переключить ПК),
        // остальное — в меню-переполнении, чтобы не перегружать шапку иконками.
        actions: [
          IconButton(icon: const Icon(Icons.refresh), onPressed: controller.refresh),
          IconButton(
            icon: const Icon(Icons.devices_outlined),
            tooltip: 'Мои ПК',
            onPressed: () => _openPcList(context),
          ),
          PopupMenuButton<_MenuAction>(
            onSelected: (action) => switch (action) {
              _MenuAction.rename => state.profile == null
                  ? null
                  : _renamePc(context, controller, state.profile!.deviceName),
              _MenuAction.unpair => _confirmUnpair(context, controller),
              _MenuAction.about => _showAbout(context),
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: _MenuAction.rename, child: Text('Переименовать ПК')),
              PopupMenuItem(value: _MenuAction.unpair, child: Text('Отвязать ПК')),
              PopupMenuDivider(),
              PopupMenuItem(value: _MenuAction.about, child: Text('О приложении')),
            ],
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: controller.refresh,
        child: ListView(
          // Обычного EdgeInsets.all(16) недостаточно на телефонах с жестовой
          // навигацией/edge-to-edge (низ экрана — системная область, не safe area
          // сама по себе прибавляет отступ только сверху под AppBar) — без явного
          // нижнего отступа последние карточки (таймеры) перекрывались системной
          // панелью снизу. См. отчёт на реальном устройстве.
          padding: EdgeInsets.fromLTRB(16, 16, 16, 16 + MediaQuery.paddingOf(context).bottom),
          children: [
            _StatusCard(online: state.online, loading: state.loading, errorMessage: state.errorMessage),
            if (state.pendingActionsCount > 0) ...[
              const SizedBox(height: 8),
              _PendingActionsBanner(count: state.pendingActionsCount),
            ],
            const SizedBox(height: 16),
            _SectionCard(title: 'Основные действия', child: _QuickCommands(controller: controller)),
            const SizedBox(height: 12),
            _SectionCard(title: 'Таймеры выключения', child: _QuickShutdownPresets(controller: controller)),
            const SizedBox(height: 12),
            _SectionCard(title: 'Громкость', child: _VolumeControls(controller: controller)),
            const SizedBox(height: 12),
            _SectionCard(title: 'Медиа', child: _MediaControls(controller: controller)),
            const SizedBox(height: 20),
            _SectionLabel('Активные задачи'),
            const SizedBox(height: 8),
            _ActiveTimersList(timers: state.timers, controller: controller),
          ],
        ),
      ),
      // Переключение на отдельный экран тачпада (курсор мыши + клавиатура ПК) — второй
      // пункт меню внизу, а не ещё одна иконка в AppBar, потому что это не действие, а
      // целый отдельный режим экрана (см. TouchpadScreen).
      bottomNavigationBar: NavigationBar(
        selectedIndex: 0,
        onDestinationSelected: (index) {
          if (index == 1) {
            Navigator.of(context).push(MaterialPageRoute(builder: (_) => TouchpadScreen(clientId: clientId)));
          }
        },
        destinations: const [
          NavigationDestination(icon: Icon(Icons.home), label: 'Управление'),
          NavigationDestination(icon: Icon(Icons.touch_app_outlined), selectedIcon: Icon(Icons.touch_app), label: 'Тачпад'),
        ],
      ),
    );
  }

  /// Открывает список сопряжённых ПК — переключиться на другой или добавить новый
  /// (docs/roadmap.md, "несколько агентов"). Отдельный экран, а не диалог, — список
  /// может расти произвольно, плюс там же живёт отзыв/переименование.
  void _openPcList(BuildContext context) {
    Navigator.of(context).push(MaterialPageRoute(builder: (_) => const PcListScreen()));
  }

  /// Локальное переименование ПК (docs/roadmap.md, "настройка переименования ПК") —
  /// как телефон подписывает этот ПК у себя, самого ПК не касается.
  void _renamePc(BuildContext context, DashboardController controller, String currentName) {
    final nameController = TextEditingController(text: currentName);
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Переименовать ПК'),
        // hintText, а не labelText — заголовок диалога уже говорит, что это за поле,
        // а плавающая labelText внутри поля на некоторых телефонах наезжала на введённый
        // текст (см. аналогичный фикс в pairing_screen.dart, _LabeledField).
        content: TextField(
          controller: nameController,
          autofocus: true,
          decoration: const InputDecoration(hintText: 'Имя ПК'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Отмена')),
          FilledButton(
            onPressed: () {
              Navigator.pop(ctx);
              controller.rename(nameController.text);
            },
            child: const Text('Сохранить'),
          ),
        ],
      ),
    );
  }

  /// Ручной разрыв сопряжения — тот же путь, что срабатывает автоматически при
  /// UNKNOWN_CLIENT (см. DashboardController), но по явному действию пользователя:
  /// например, если он хочет перепривязать телефон к другому ПК или к тому же ПК
  /// заново после смены PIN.
  void _confirmUnpair(BuildContext context, DashboardController controller) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Отвязать ПК?'),
        content: const Text(
          'Приложение забудет этот ПК — придётся сопрягаться заново (QR-код или PIN). '
          'Само сопряжение на стороне ПК тоже нужно отозвать отдельно, если вы хотите '
          'полностью прекратить ему доступ.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Отмена')),
          FilledButton(
            onPressed: () {
              Navigator.pop(ctx);
              controller.unpair();
            },
            child: const Text('Отвязать'),
          ),
        ],
      ),
    );
  }
}

enum _MenuAction { rename, unpair, about }

/// Показывает версию/авторство приложения — стандартный `showAboutDialog` (даёт
/// системный вид "О программе": иконка, имя, версия, кнопка "Лицензии" со списком
/// пакетов) вместо самодельного диалога.
void _showAbout(BuildContext context) {
  showAboutDialog(
    context: context,
    applicationName: AppInfo.name,
    applicationVersion: AppInfo.version,
    applicationLegalese: '© ${AppInfo.author}',
  );
}

/// Заголовок раздела — единый стиль вместо titleSmall вперемешку с разным цветом
/// по всему экрану (см. скилл mobile-android-design: типографика через тему, а не
/// точечно).
class _SectionLabel extends StatelessWidget {
  final String text;
  const _SectionLabel(this.text);

  @override
  Widget build(BuildContext context) {
    return Text(
      text,
      style: Theme.of(context).textTheme.labelLarge?.copyWith(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
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
    final scheme = Theme.of(context).colorScheme;
    // Тональная пара container/onContainer вместо сырых Colors.green/red — так
    // карточка остаётся читаемой и в тёмной теме (см. AppTheme.AppStatusColors).
    final containerColor = loading
        ? scheme.surfaceContainerHighest
        : (online ? scheme.onlineContainer : scheme.errorContainer);
    final onContainerColor =
        loading ? scheme.onSurfaceVariant : (online ? scheme.onOnlineContainer : scheme.onErrorContainer);
    final label = loading ? 'Проверка…' : (online ? 'ПК онлайн' : 'ПК недоступен');
    final icon = loading ? Icons.hourglass_top : (online ? Icons.check_circle : Icons.error_outline);

    return Card(
      color: containerColor,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Icon(icon, color: onContainerColor, size: 22),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                label,
                style: Theme.of(context).textTheme.titleMedium?.copyWith(color: onContainerColor),
              ),
            ),
            if (loading)
              SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(strokeWidth: 2, color: onContainerColor),
              ),
          ],
        ),
      ),
    );
  }
}

/// Показывает, что часть действий ещё не доставлена на ПК (связь временно терялась) —
/// см. docs/roadmap.md, "Устойчивость к потере соединения". Отправятся автоматически
/// при следующем удачном подключении, отдельно нажимать ничего не нужно.
class _PendingActionsBanner extends StatelessWidget {
  final int count;
  const _PendingActionsBanner({required this.count});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      color: scheme.secondaryContainer,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(
          children: [
            Icon(Icons.cloud_off, size: 18, color: scheme.onSecondaryContainer),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                'Ожидает отправки на ПК: $count',
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: scheme.onSecondaryContainer),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Карточка-раздел с заголовком — единый контейнер вместо голого списка виджетов
/// вперемешку с текстовыми ярлыками (см. референс дизайна: сгруппированные блоки).
class _SectionCard extends StatelessWidget {
  final String title;
  final Widget child;
  const _SectionCard({required this.title, required this.child});

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),
            child,
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
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Выключение — самое частое и самое необратимое действие здесь, поэтому оно
        // одно, крупное, во весь ряд и в сплошном "тревожном" цвете; всё остальное —
        // мельче и в один общий ряд ниже (по M3 сплошной error оставляют только для
        // самого критичного действия).
        FilledButton.icon(
          onPressed: () => _confirmAndRun(context, 'Выключить ПК сейчас?', controller.shutdownNow),
          icon: const Icon(Icons.power_settings_new),
          label: const Text('Выключить'),
          style: FilledButton.styleFrom(
            backgroundColor: Theme.of(context).colorScheme.error,
            foregroundColor: Theme.of(context).colorScheme.onError,
            padding: const EdgeInsets.symmetric(vertical: 18),
            textStyle: Theme.of(context).textTheme.titleMedium,
          ),
        ),
        const SizedBox(height: 10),
        // 2 колонки одинаковой ширины (не Wrap — там ширина кнопки зависит от длины
        // подписи, и "Гибернация" получалась заметно шире "Сна", ряды не выравнивались).
        _TwoColumnGrid(
          spacing: 8,
          children: [
            _SmallTonalButton(
              icon: Icons.restart_alt,
              label: 'Перезагрузка',
              onPressed: () => _confirmAndRun(context, 'Перезагрузить ПК?', controller.restartNow),
            ),
            _SmallTonalButton(icon: Icons.bedtime, label: 'Сон', onPressed: controller.sleepNow),
            _SmallTonalButton(icon: Icons.ac_unit, label: 'Гибернация', onPressed: controller.hibernateNow),
            _SmallTonalButton(icon: Icons.lock, label: 'Заблокировать', onPressed: controller.lockNow),
          ],
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

/// Второстепенная команда — заметно мельче основной кнопки "Выключить" (меньше
/// отступы и размер текста/иконки), но остаётся полноценной кнопкой с тем же
/// filled-tonal стилем, а не просто иконкой без подписи.
class _SmallTonalButton extends StatelessWidget {
  final IconData icon;
  final String label;
  final VoidCallback? onPressed;
  const _SmallTonalButton({required this.icon, required this.label, required this.onPressed});

  @override
  Widget build(BuildContext context) {
    return FilledButton.tonalIcon(
      onPressed: onPressed,
      icon: Icon(icon, size: 16),
      label: Text(label, overflow: TextOverflow.ellipsis),
      style: FilledButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        textStyle: Theme.of(context).textTheme.bodySmall,
        visualDensity: VisualDensity.compact,
        alignment: Alignment.centerLeft,
      ),
    );
  }
}

/// Раскладывает кнопки по 2 в ряд одинаковой ширины (ячейка = половина ширины
/// родителя, независимо от длины подписи внутри) — для Wrap ширина каждой кнопки
/// зависела от её текста, и ряды визуально не выравнивались (см. вызов в _QuickCommands).
class _TwoColumnGrid extends StatelessWidget {
  final List<Widget> children;
  final double spacing;
  const _TwoColumnGrid({required this.children, this.spacing = 8});

  @override
  Widget build(BuildContext context) {
    final rows = <Widget>[];
    for (var i = 0; i < children.length; i += 2) {
      final hasSecond = i + 1 < children.length;
      if (rows.isNotEmpty) rows.add(SizedBox(height: spacing));
      // IntrinsicHeight — иначе Row с crossAxisAlignment.stretch внутри ListView
      // (неограниченная по высоте ось) пытается растянуть кнопки на бесконечную
      // высоту и падает с "RenderBox was not laid out" (поймано на эмуляторе).
      rows.add(IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(child: children[i]),
            SizedBox(width: spacing),
            Expanded(child: hasSecond ? children[i + 1] : const SizedBox.shrink()),
          ],
        ),
      ));
    }
    return Column(children: rows);
  }
}

/// Пресеты отложенного выключения прямо на главном экране — одним тапом, без
/// модальных диалогов и перехода на отдельный экран таймера. Частые интервалы
/// (15/30/60 мин) — крупные кнопки в один тап; более редкие и длинные (1.5/2/3 часа)
/// — мельче и подписаны в часах, а не в "непонятных" минутах вроде "180 мин".
class _QuickShutdownPresets extends StatelessWidget {
  final DashboardController controller;
  const _QuickShutdownPresets({required this.controller});

  static const _bigPresetsMinutes = [15, 30, 60];
  static const _smallPresetsMinutes = [90, 120, 180];

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            for (final minutes in _bigPresetsMinutes) ...[
              if (minutes != _bigPresetsMinutes.first) const SizedBox(width: 8),
              Expanded(
                child: FilledButton.tonal(
                  onPressed: () => controller.scheduleShutdownIn(minutes),
                  style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 14)),
                  child: Text('$minutes мин'),
                ),
              ),
            ],
          ],
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: [
            for (final minutes in _smallPresetsMinutes)
              ActionChip(
                label: Text(_hoursLabel(minutes)),
                onPressed: () => controller.scheduleShutdownIn(minutes),
              ),
            ActionChip(
              avatar: const Icon(Icons.schedule, size: 18),
              label: const Text('Своё время'),
              onPressed: () => _pickCustomTime(context, controller),
            ),
          ],
        ),
      ],
    );
  }

  String _hoursLabel(int minutes) {
    final hours = minutes / 60;
    final text = hours == hours.roundToDouble() ? hours.toStringAsFixed(0) : hours.toStringAsFixed(1);
    return '$text ч';
  }

  /// Выключение на конкретное время (не интервал от "сейчас") — если выбранное время
  /// сегодня уже прошло, планируем на завтра и явно говорим об этом в подтверждении,
  /// чтобы не запланировать выключение "уже прошедшим" временем по ошибке.
  Future<void> _pickCustomTime(BuildContext context, DashboardController controller) async {
    final now = TimeOfDay.now();
    final picked = await showTimePicker(context: context, initialTime: now);
    if (picked == null) return;

    var scheduled = DateTime(DateTime.now().year, DateTime.now().month, DateTime.now().day, picked.hour, picked.minute);
    final isTomorrow = !scheduled.isAfter(DateTime.now());
    if (isTomorrow) scheduled = scheduled.add(const Duration(days: 1));

    if (!context.mounted) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Подтверждение'),
        content: Text(
          isTomorrow
              ? 'Выключить ПК завтра в ${picked.format(context)}?'
              : 'Выключить ПК сегодня в ${picked.format(context)}?',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Отмена')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Запланировать')),
        ],
      ),
    );
    if (confirmed == true) controller.scheduleShutdownAt(scheduled);
  }
}

/// Кнопки громкости/медиа сделаны заметно крупнее обычных IconButton (48x48 по
/// умолчанию) — их часто нажимают не глядя, на ходу, и мелкая цель тут не по месту.
class _BigIconButton extends StatelessWidget {
  final IconData icon;
  final VoidCallback? onPressed;
  final bool filled;
  const _BigIconButton({required this.icon, required this.onPressed, this.filled = false});

  static const _size = 64.0;

  @override
  Widget build(BuildContext context) {
    final style = IconButton.styleFrom(minimumSize: const Size(_size, _size));
    return filled
        ? IconButton.filled(icon: Icon(icon, size: 28), onPressed: onPressed, style: style)
        : IconButton.filledTonal(icon: Icon(icon, size: 28), onPressed: onPressed, style: style);
  }
}

class _VolumeControls extends StatelessWidget {
  final DashboardController controller;
  const _VolumeControls({required this.controller});

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceEvenly,
      children: [
        _BigIconButton(icon: Icons.volume_down, onPressed: () => controller.adjustVolume('down')),
        _BigIconButton(icon: Icons.volume_up, onPressed: () => controller.adjustVolume('up')),
        _BigIconButton(icon: Icons.volume_off, onPressed: () => controller.adjustVolume('mute')),
      ],
    );
  }
}

/// Плей/пауза и переключение треков — управление воспроизведением на ПК отдельно от
/// громкости (эмуляция медиаклавиш, см. windows-agent MediaControlService). Плей и
/// пауза — одна и та же клавиша-переключатель на стороне Windows, поэтому это одна
/// кнопка, а не раздельные "Играть"/"Пауза".
class _MediaControls extends StatelessWidget {
  final DashboardController controller;
  const _MediaControls({required this.controller});

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceEvenly,
      children: [
        _BigIconButton(icon: Icons.skip_previous, onPressed: controller.mediaPrevious),
        _BigIconButton(icon: Icons.play_arrow, onPressed: controller.mediaPlayPause, filled: true),
        _BigIconButton(icon: Icons.skip_next, onPressed: controller.mediaNext),
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
      return Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Text(
            'Активных задач нет.',
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
          ),
        ),
      );
    }

    return Column(
      children: timers
          .map((t) => Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Card(
                  child: ListTile(
                    leading: _TonalIcon(_actionIcon(t.action)),
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
                ),
              ))
          .toList(),
    );
  }

  IconData _actionIcon(TimerAction action) => switch (action) {
        TimerAction.shutdown => Icons.power_settings_new,
        TimerAction.restart => Icons.restart_alt,
      };

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

/// Иконка в тональном кружке — стандартный M3-паттерн для лидирующей иконки в списке
/// (см. скилл mobile-android-design, пример ItemListCard) вместо голой Icon().
class _TonalIcon extends StatelessWidget {
  final IconData icon;
  const _TonalIcon(this.icon);

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return CircleAvatar(
      backgroundColor: scheme.primaryContainer,
      foregroundColor: scheme.onPrimaryContainer,
      child: Icon(icon, size: 20),
    );
  }
}
