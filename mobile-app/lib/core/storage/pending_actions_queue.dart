import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// Отложенное действие с таймером, которое не удалось сразу доставить на ПК
/// (docs/roadmap.md, "Устойчивость к потере соединения"). Очередь — это не источник
/// истины по таймерам (им остаётся SQLite на ПК), а способ не потерять команду
/// пользователя, если в момент нажатия телефон не смог достучаться до агента.
sealed class PendingAction {
  const PendingAction();

  Map<String, dynamic> toJson();

  static PendingAction fromJson(Map<String, dynamic> json) {
    return switch (json['type'] as String) {
      'scheduleShutdown' => ScheduleShutdownAction(json['minutes'] as int),
      'cancelTimer' => CancelTimerAction(json['timerId'] as String),
      'snoozeTimer' => SnoozeTimerAction(json['timerId'] as String, json['minutes'] as int),
      final other => throw FormatException('Неизвестный тип отложенного действия: $other'),
    };
  }
}

class ScheduleShutdownAction extends PendingAction {
  final int minutes;
  const ScheduleShutdownAction(this.minutes);

  @override
  Map<String, dynamic> toJson() => {'type': 'scheduleShutdown', 'minutes': minutes};
}

class CancelTimerAction extends PendingAction {
  final String timerId;
  const CancelTimerAction(this.timerId);

  @override
  Map<String, dynamic> toJson() => {'type': 'cancelTimer', 'timerId': timerId};
}

class SnoozeTimerAction extends PendingAction {
  final String timerId;
  final int minutes;
  const SnoozeTimerAction(this.timerId, this.minutes);

  @override
  Map<String, dynamic> toJson() => {'type': 'snoozeTimer', 'timerId': timerId, 'minutes': minutes};
}

/// Персистентная (shared_preferences) очередь [PendingAction] — переживает перезапуск
/// приложения, чтобы действие не потерялось, если телефон разрядился/приложение
/// закрылось до того, как связь с ПК восстановилась.
///
/// Одна очередь на clientId — при нескольких сопряжённых ПК (docs/roadmap.md,
/// "несколько агентов") действия одного ПК не должны уходить другому при следующем
/// удачном подключении.
class PendingActionsQueue {
  final String clientId;

  PendingActionsQueue(this.clientId);

  String get _prefsKey => 'pending_actions_queue_$clientId';

  Future<List<PendingAction>> load() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_prefsKey);
    if (raw == null) return [];
    final list = jsonDecode(raw) as List<dynamic>;
    return list.map((e) => PendingAction.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<void> _save(List<PendingAction> actions) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefsKey, jsonEncode(actions.map((a) => a.toJson()).toList()));
  }

  Future<void> enqueue(PendingAction action) async {
    final actions = await load();
    actions.add(action);
    await _save(actions);
  }

  Future<void> removeAt(int index) async {
    final actions = await load();
    if (index < 0 || index >= actions.length) return;
    actions.removeAt(index);
    await _save(actions);
  }

  Future<void> clear() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_prefsKey);
  }
}
