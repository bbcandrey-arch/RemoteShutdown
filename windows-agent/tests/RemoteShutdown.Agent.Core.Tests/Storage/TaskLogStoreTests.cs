using RemoteShutdown.Agent.Core.Storage;
using Xunit;

namespace RemoteShutdown.Agent.Core.Tests.Storage;

/// <summary>
/// Журнал задач (docs/roadmap.md, "Уведомления и журнал задач") — источник и для
/// баллон-уведомлений в трее (поллинг ListSince), и для вкладки "Журнал" в настройках.
/// </summary>
public class TaskLogStoreTests
{
    private static TaskLogStore CreateStore()
    {
        var db = new AgentDatabase(Path.Combine(Path.GetTempPath(), $"tasklog-test-{Guid.NewGuid():N}.db"));
        db.EnsureCreated();
        return new TaskLogStore(db);
    }

    [Fact]
    public void Add_ThenListRecent_ReturnsNewestFirst()
    {
        var store = CreateStore();
        store.Add("command", "Выключение сейчас");
        store.Add("timerCreated", "Запланирован таймер: выключение в 20:00");

        var recent = store.ListRecent();

        Assert.Equal(2, recent.Count);
        Assert.Equal("Запланирован таймер: выключение в 20:00", recent[0].Description);
        Assert.Equal("Выключение сейчас", recent[1].Description);
    }

    [Fact]
    public void ListSince_OnlyReturnsEntriesAfterGivenId_InAscendingOrder()
    {
        var store = CreateStore();
        store.Add("command", "первая");
        var afterFirst = store.GetMaxId();
        store.Add("command", "вторая");
        store.Add("command", "третья");

        var since = store.ListSince(afterFirst);

        Assert.Equal(2, since.Count);
        Assert.Equal("вторая", since[0].Description);
        Assert.Equal("третья", since[1].Description);
    }

    [Fact]
    public void GetMaxId_OnEmptyLog_ReturnsZero()
    {
        var store = CreateStore();
        Assert.Equal(0, store.GetMaxId());
    }
}
