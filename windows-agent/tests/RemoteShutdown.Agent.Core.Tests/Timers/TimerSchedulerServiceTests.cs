using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteShutdown.Agent.Core.Events;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Storage;
using RemoteShutdown.Agent.Core.Timers;

namespace RemoteShutdown.Agent.Core.Tests.Timers;

public class TimerSchedulerServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"agent-tests-{Guid.NewGuid():N}.db");
    private readonly TimerRepository _repository;

    public TimerSchedulerServiceTests()
    {
        var db = new AgentDatabase(_dbPath);
        db.EnsureCreated();
        _repository = new TimerRepository(db);
    }

    private TimerSchedulerService CreateScheduler(TaskLogStore? taskLog = null) =>
        new(_repository, new PowerActionsService(), new NullAgentEventPublisher(), NullLogger<TimerSchedulerService>.Instance, taskLog);

    [Fact]
    public void Create_persists_the_timer_as_pending()
    {
        using var scheduler = CreateScheduler();

        var timer = scheduler.Create(ScheduledAction.Shutdown, DateTime.UtcNow.AddMinutes(30), "client-1");

        var stored = _repository.Find(timer.TimerId);
        Assert.NotNull(stored);
        Assert.Equal(TimerStatus.Pending, stored!.Status);
    }

    [Fact]
    public void Cancel_marks_the_timer_cancelled_and_it_does_not_fire()
    {
        using var scheduler = CreateScheduler();
        var timer = scheduler.Create(ScheduledAction.Shutdown, DateTime.UtcNow.AddMinutes(30), "client-1");

        var cancelled = scheduler.Cancel(timer.TimerId);

        Assert.Equal(TimerStatus.Cancelled, cancelled!.Status);
        Assert.Equal(TimerStatus.Cancelled, _repository.Find(timer.TimerId)!.Status);
    }

    [Fact]
    public void Snooze_pushes_the_schedule_forward_by_the_given_minutes()
    {
        using var scheduler = CreateScheduler();
        var original = DateTime.UtcNow.AddMinutes(30);
        var timer = scheduler.Create(ScheduledAction.Shutdown, original, "client-1");

        var snoozed = scheduler.Snooze(timer.TimerId, 10);

        Assert.True(Math.Abs((snoozed!.ScheduledAtUtc - original.AddMinutes(10)).TotalSeconds) < 1);
    }

    [Fact]
    public void RestoreFromStorage_rearms_pending_timers_after_a_restart()
    {
        using (var scheduler = CreateScheduler())
        {
            scheduler.Create(ScheduledAction.Shutdown, DateTime.UtcNow.AddMinutes(30), "client-1");
        }

        // Simulate the agent restarting: a fresh in-memory scheduler over the same DB.
        using var restored = CreateScheduler();
        restored.RestoreFromStorage();

        var pending = _repository.ListPending();
        Assert.Single(pending);
    }

    [Fact]
    public void Cancel_on_an_already_fired_timer_is_a_no_op()
    {
        using var scheduler = CreateScheduler();
        var timer = scheduler.Create(ScheduledAction.Shutdown, DateTime.UtcNow.AddMinutes(30), "client-1");
        _repository.UpdateStatus(timer.TimerId, TimerStatus.Fired);

        var result = scheduler.Cancel(timer.TimerId);

        Assert.Equal(TimerStatus.Fired, result!.Status);
    }

    [Fact]
    public void Create_and_Cancel_write_entries_to_the_task_log_when_one_is_wired_up()
    {
        var db = new AgentDatabase(_dbPath);
        var taskLog = new TaskLogStore(db);
        using var scheduler = CreateScheduler(taskLog);

        var timer = scheduler.Create(ScheduledAction.Shutdown, DateTime.UtcNow.AddMinutes(30), "client-1");
        scheduler.Cancel(timer.TimerId);

        var entries = taskLog.ListRecent();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Kind == "timerCreated");
        Assert.Contains(entries, e => e.Kind == "timerCancelled");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
