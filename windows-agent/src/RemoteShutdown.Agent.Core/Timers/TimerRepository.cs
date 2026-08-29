using Microsoft.Data.Sqlite;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Core.Timers;

public sealed class TimerRepository
{
    private readonly AgentDatabase _db;

    public TimerRepository(AgentDatabase db) => _db = db;

    public void Insert(ScheduledTimer timer)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Timers (TimerId, Action, ScheduledAtUtc, Status, CreatedByClientId, CreatedAtUtc)
            VALUES ($id, $action, $scheduledAt, $status, $clientId, $createdAt)
            """;
        Bind(cmd, timer);
        cmd.ExecuteNonQuery();
    }

    public void UpdateStatus(string timerId, TimerStatus status)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Timers SET Status = $status WHERE TimerId = $id";
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$id", timerId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateSchedule(string timerId, DateTime scheduledAtUtc)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Timers SET ScheduledAtUtc = $scheduledAt, Status = 'Pending' WHERE TimerId = $id";
        cmd.Parameters.AddWithValue("$scheduledAt", scheduledAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$id", timerId);
        cmd.ExecuteNonQuery();
    }

    public ScheduledTimer? Find(string timerId)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TimerId, Action, ScheduledAtUtc, Status, CreatedByClientId, CreatedAtUtc FROM Timers WHERE TimerId = $id";
        cmd.Parameters.AddWithValue("$id", timerId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<ScheduledTimer> ListPending()
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TimerId, Action, ScheduledAtUtc, Status, CreatedByClientId, CreatedAtUtc FROM Timers WHERE Status = 'Pending' ORDER BY ScheduledAtUtc";
        using var reader = cmd.ExecuteReader();
        var result = new List<ScheduledTimer>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public IReadOnlyList<ScheduledTimer> ListAll()
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TimerId, Action, ScheduledAtUtc, Status, CreatedByClientId, CreatedAtUtc FROM Timers ORDER BY ScheduledAtUtc";
        using var reader = cmd.ExecuteReader();
        var result = new List<ScheduledTimer>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    private static void Bind(SqliteCommand cmd, ScheduledTimer timer)
    {
        cmd.Parameters.AddWithValue("$id", timer.TimerId);
        cmd.Parameters.AddWithValue("$action", timer.Action.ToString());
        cmd.Parameters.AddWithValue("$scheduledAt", timer.ScheduledAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$status", timer.Status.ToString());
        cmd.Parameters.AddWithValue("$clientId", timer.CreatedByClientId);
        cmd.Parameters.AddWithValue("$createdAt", timer.CreatedAtUtc.ToString("O"));
    }

    private static ScheduledTimer Map(SqliteDataReader reader) => new(
        TimerId: reader.GetString(0),
        Action: Enum.Parse<ScheduledAction>(reader.GetString(1)),
        ScheduledAtUtc: DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
        Status: Enum.Parse<TimerStatus>(reader.GetString(3)),
        CreatedByClientId: reader.GetString(4),
        CreatedAtUtc: DateTime.Parse(reader.GetString(5)).ToUniversalTime());
}
