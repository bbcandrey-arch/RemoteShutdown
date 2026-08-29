namespace RemoteShutdown.Agent.Core.Timers;

public enum ScheduledAction { Shutdown, Restart }

public enum TimerStatus { Pending, Fired, Cancelled, Missed }

public sealed record ScheduledTimer(
    string TimerId,
    ScheduledAction Action,
    DateTime ScheduledAtUtc,
    TimerStatus Status,
    string CreatedByClientId,
    DateTime CreatedAtUtc);
