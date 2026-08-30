import 'package:flutter/material.dart';

import '../../core/security/device_profile_store.dart';
import '../../core/security/secure_storage_service.dart';
import '../../core/storage/pending_actions_queue.dart';
import '../../models/device_profile.dart';
import '../dashboard/dashboard_screen.dart';
import '../pairing/pairing_screen.dart';

/// Список всех сопряжённых ПК (docs/roadmap.md, "несколько агентов" — приложение
/// умеет держать сколько угодно пар одновременно, не только одну): переключение
/// между ними, переименование, отвязка и добавление ещё одного. Открывается из
/// Dashboard по иконке "Мои ПК" в AppBar.
class PcListScreen extends StatefulWidget {
  const PcListScreen({super.key});

  @override
  State<PcListScreen> createState() => _PcListScreenState();
}

class _PcListScreenState extends State<PcListScreen> {
  final _profileStore = DeviceProfileStore();
  List<DeviceProfile> _profiles = [];
  String? _lastUsedId;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final profiles = await _profileStore.loadAll();
    final lastUsed = await _profileStore.getLastUsedClientId();
    if (!mounted) return;
    setState(() {
      _profiles = profiles;
      _lastUsedId = lastUsed;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Мои ПК')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _profiles.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Text(
                      'Сопряжённых ПК пока нет.',
                      style: Theme.of(context)
                          .textTheme
                          .bodyMedium
                          ?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant),
                    ),
                  ),
                )
              : ListView.builder(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  itemCount: _profiles.length,
                  itemBuilder: (context, index) {
                    final profile = _profiles[index];
                    final isLastUsed = profile.clientId == _lastUsedId;
                    final scheme = Theme.of(context).colorScheme;
                    return Padding(
                      padding: const EdgeInsets.only(bottom: 8),
                      child: Card(
                        child: ListTile(
                          leading: CircleAvatar(
                            backgroundColor: isLastUsed ? scheme.primaryContainer : scheme.surfaceContainerHighest,
                            foregroundColor: isLastUsed ? scheme.onPrimaryContainer : scheme.onSurfaceVariant,
                            child: const Icon(Icons.computer),
                          ),
                          title: Text(profile.deviceName),
                          subtitle: Text('${profile.host}:${profile.port}'),
                          onTap: () => openDashboardForPc(context, profile.clientId),
                          trailing: PopupMenuButton<String>(
                            onSelected: (value) {
                              if (value == 'rename') _rename(profile);
                              if (value == 'remove') _remove(profile);
                            },
                            itemBuilder: (_) => const [
                              PopupMenuItem(value: 'rename', child: Text('Переименовать')),
                              PopupMenuItem(value: 'remove', child: Text('Отвязать')),
                            ],
                          ),
                        ),
                      ),
                    );
                  },
                ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => Navigator.of(context).push(
          MaterialPageRoute(
            builder: (_) => PairingScreen(onPaired: (clientId) => openDashboardForPc(context, clientId)),
          ),
        ),
        icon: const Icon(Icons.add),
        label: const Text('Добавить ПК'),
      ),
    );
  }

  Future<void> _rename(DeviceProfile profile) async {
    final controller = TextEditingController(text: profile.deviceName);
    final newName = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Переименовать ПК'),
        content: TextField(controller: controller, autofocus: true, decoration: const InputDecoration(labelText: 'Имя ПК')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Отмена')),
          FilledButton(onPressed: () => Navigator.pop(ctx, controller.text), child: const Text('Сохранить')),
        ],
      ),
    );
    if (newName == null || newName.trim().isEmpty) return;
    await _profileStore.rename(profile.clientId, newName.trim());
    await _load();
  }

  Future<void> _remove(DeviceProfile profile) async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Отвязать «${profile.deviceName}»?'),
        content: const Text(
          'Приложение забудет этот ПК — придётся сопрягаться заново. Само сопряжение на '
          'стороне ПК тоже нужно отозвать отдельно, если хотите полностью прекратить ему доступ.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Отмена')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Отвязать')),
        ],
      ),
    );
    if (confirm != true) return;

    await SecureStorageService().deleteSharedSecret(profile.clientId);
    await _profileStore.remove(profile.clientId);
    await PendingActionsQueue(profile.clientId).clear();
    await _load();
  }
}

/// Открывает Dashboard указанного ПК, заменяя весь стек навигации (чтобы кнопка
/// "назад" не вела обратно в список/пейринг) — используется при выборе ПК из списка и
/// сразу после успешного добавления нового. Если этот ПК потом отвяжется прямо с
/// Dashboard, [_afterUnpair] решает, куда вернуться.
void openDashboardForPc(BuildContext context, String clientId) {
  Navigator.of(context).pushAndRemoveUntil(
    MaterialPageRoute(
      builder: (_) => DashboardScreen(clientId: clientId, onUnpaired: () => _afterUnpair(context)),
    ),
    (route) => false,
  );
}

Future<void> _afterUnpair(BuildContext context) async {
  // DeviceProfileStore.remove() уже передвинул "последний использованный" на другой
  // оставшийся ПК, если он есть, — открываем его напрямую, а не заставляем выбирать
  // заново из списка (см. docs/roadmap.md, "открывать последний использованный").
  final lastUsed = await DeviceProfileStore().loadLastUsed();
  if (!context.mounted) return;
  if (lastUsed != null) {
    openDashboardForPc(context, lastUsed.clientId);
  } else {
    Navigator.of(context).pushAndRemoveUntil(
      MaterialPageRoute(builder: (_) => PairingScreen(onPaired: (clientId) => openDashboardForPc(context, clientId))),
      (route) => false,
    );
  }
}
