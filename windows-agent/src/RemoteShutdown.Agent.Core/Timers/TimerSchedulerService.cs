using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RemoteShutdown.Agent.Core.Events;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Core.Timers;

/// <summary>
/// Owns in-memory scheduling for pending timers, backed by SQLite (TimerRepository) as
/// the source of truth so timers survive an agent restart (docs/protocol.md §7,
/// architecture plan Этап 2 "Хранение состояния таймеров").
/// </summary>
public sealed class TimerSchedulerService : IDisposable
{
    private readonly TimerRepository _repository;
    private readonly PowerActionsService _powerActions;
    private readonly IAgentEventPublisher _events;
    private readonly ILogger<TimerSchedulerService> _logger;
    private readonly TaskLogStore? _taskLog;
    private readonly ConcurrentDictionary<string, System.Threading.Timer> _activeTimers = new();

    /// <summary>Whether a timer that should have fired while the agent was down gets run immediately on restore, or marked Missed.</summary>
    public bool RunMissedOnStartup { get; set; } = true;

    /// <param name="taskLog">
    /// Необязателен (по умолчанию null) — сделан опциональным, чтобы существующие
    /// юнит-тесты не тянули за собой реальную SQLite-БД ради журнала задач.
    /// </param>
    public TimerSchedulerService(
        TimerRepository repository,
        PowerActionsService powerActions,
        IAgentEventPublisher events,
        ILogger<TimerSchedulerService> logger,
        TaskLogStore? taskLog = null)
    {
        _repository = repository;
        _powerActions = powerActions;
        _events = events;
        _logger = logger;
        _taskLog = taskLog;
    }

    /// <summary>Call once at startup to reload pending timers from SQLite and re-arm them.</summary>
    public void RestoreFromStorage()
    {
        foreach (var timer in _repository.ListPending())
        {
            var remaining = timer.ScheduledAtUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                if (RunMissedOnStartup)
                    Fire(timer.TimerId);
                else
                    _repository.UpdateStatus(timer.TimerId, TimerStatus.Missed);
                continue;
            }

            Arm(timer.TimerId, remaining);
        }
    }

    public ScheduledTimer Create(ScheduledAction action, DateTime scheduledAtUtc, string createdByClientId)
    {
        var timer = new ScheduledTimer(
            TimerId: Guid.NewGuid().ToString(),
            Action: action,
            ScheduledAtUtc: scheduledAtUtc,
            Status: TimerStatus.Pending,
            CreatedByClientId: createdByClientId,
            CreatedAtUtc: DateTime.UtcNow);

        _repository.Insert(timer);
        Arm(timer.TimerId, scheduledAtUtc - DateTime.UtcNow);

        var when = scheduledAtUtc.ToLocalTime().ToString("dd.MM HH:mm");
        _taskLog?.Add("timerCreated", $"Запланирован таймер: {ActionLabel(action)} в {when}", createdByClientId);

        return timer;
    }

    public IReadOnlyList<ScheduledTimer> ListAll() => _repository.ListAll();

    public ScheduledTimer? Cancel(string timerId)
    {
        var timer = _repository.Find(timerId);
        if (timer is null || timer.Status != TimerStatus.Pending) return timer;

        Disarm(timerId);
        _repository.UpdateStatus(timerId, TimerStatus.Cancelled);
        _ = _events.PublishAsync("timerCancelled", new { timerId });
        _taskLog?.Add("timerCancelled", $"Отменён таймер: {ActionLabel(timer.Action)}");
        return timer with { Status = TimerStatus.Cancelled };
    }

    public ScheduledTimer? Reschedule(string timerId, DateTime newScheduledAtUtc)
    {
        var timer = _repository.Find(timerId);
        if (timer is null || timer.Status != TimerStatus.Pending) return timer;

        Disarm(timerId);
        _repository.UpdateSchedule(timerId, newScheduledAtUtc);
        Arm(timerId, newScheduledAtUtc - DateTime.UtcNow);

        var when = newScheduledAtUtc.ToLocalTime().ToString("dd.MM HH:mm");
        _taskLog?.Add("timerUpdated", $"Перенесён таймер: {ActionLabel(timer.Action)} на {when}");

        return timer with { ScheduledAtUtc = newScheduledAtUtc, Status = TimerStatus.Pending };
    }

    public ScheduledTimer? Snooze(string timerId, int minutes)
    {
        var timer = _repository.Find(timerId);
        if (timer is null || timer.Status != TimerStatus.Pending) return timer;

        return Reschedule(timerId, timer.ScheduledAtUtc + TimeSpan.FromMinutes(minutes));
    }

    private void Arm(string timerId, TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        var timer = new System.Threading.Timer(_ => Fire(timerId), null, delay, Timeout.InfiniteTimeSpan);
        _activeTimers[timerId] = timer;
    }

    private void Disarm(string timerId)
    {
        if (_activeTimers.TryRemove(timerId, out var timer))
            timer.Dispose();
    }

    private void Fire(string timerId)
    {
        _activeTimers.TryRemove(timerId, out var handle);
        handle?.Dispose();

        var timer = _repository.Find(timerId);
        if (timer is null || timer.Status != TimerStatus.Pending) return;

        try
        {
            _powerActions.Execute(timer.Action == ScheduledAction.Shutdown ? PowerAction.Shutdown : PowerAction.Restart);
            _repository.UpdateStatus(timerId, TimerStatus.Fired);
            _ = _events.PublishAsync("timerFired", new { timerId, action = timer.Action.ToString() });
            _taskLog?.Add("timerFired", $"Сработал таймер: {ActionLabel(timer.Action)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute timer {TimerId}", timerId);
            _ = _events.PublishAsync("error", new { timerId, message = ex.Message });
            _taskLog?.Add("error", $"Ошибка выполнения таймера ({ActionLabel(timer.Action)}): {ex.Message}");
        }
    }

    private static string ActionLabel(ScheduledAction action) => action switch
    {
        ScheduledAction.Shutdown => "выключение",
        ScheduledAction.Restart => "перезагрузка",
        _ => action.ToString(),
    };

    public void Dispose()
    {
        foreach (var timer in _activeTimers.Values)
            timer.Dispose();
        _activeTimers.Clear();
    }
}
