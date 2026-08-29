/// Зеркалит ScheduledTimer на агенте (windows-agent/.../Timers/TimerModels.cs) и формат
/// ответа /timers из docs/protocol.md §7.
enum TimerAction { shutdown, restart }

enum TimerStatus { pending, fired, cancelled, missed }

class TimerTask {
  final String timerId;
  final TimerAction action;
  final DateTime scheduledAtUtc;
  final TimerStatus status;

  const TimerTask({
    required this.timerId,
    required this.action,
    required this.scheduledAtUtc,
    required this.status,
  });

  factory TimerTask.fromJson(Map<String, dynamic> json) => TimerTask(
        timerId: json['timerId'] as String,
        action: _actionFromString(json['action'] as String),
        scheduledAtUtc: DateTime.parse(json['scheduledAtUtc'] as String).toUtc(),
        status: _statusFromString(json['status'] as String),
      );

  static TimerAction _actionFromString(String value) => TimerAction.values.firstWhere(
        (a) => a.name.toLowerCase() == value.toLowerCase(),
        orElse: () => TimerAction.shutdown,
      );

  static TimerStatus _statusFromString(String value) => TimerStatus.values.firstWhere(
        (s) => s.name.toLowerCase() == value.toLowerCase(),
        orElse: () => TimerStatus.pending,
      );
}
