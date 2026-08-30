using Microsoft.Data.Sqlite;

namespace RemoteShutdown.Agent.Core.Storage;

public sealed record TaskLogEntry(
    int Id,
    DateTime OccurredAtUtc,
    string Kind,
    string Description,
    string? ClientId,
    string? DeviceName,
    string? ClientIp);

/// <summary>
/// Журнал задач, прилетевших с телефона (docs/roadmap.md, "Уведомления и журнал задач"):
/// команды питания, создание/отмена/перенос таймеров, срабатывание таймера. Пишется из
/// Api-эндпоинтов и TimerSchedulerService в момент события; читается треем — как для
/// баллон-уведомлений (поллинг новых Id), так и для вкладки "Журнал" в настройках.
/// Отдельная таблица, а не переиспользование Timers/PairedDevices — это именно лог
/// событий, а не состояние (таймер может быть один, а записей о нём в журнале несколько:
/// создан, потом отменён).
/// </summary>
public sealed class TaskLogStore
{
    private readonly AgentDatabase _db;

    public TaskLogStore(AgentDatabase db) => _db = db;

    public void Add(string kind, string description, string? clientId = null, string? deviceName = null, string? clientIp = null)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TaskLog (OccurredAtUtc, Kind, Description, ClientId, DeviceName, ClientIp)
            VALUES ($occurredAt, $kind, $description, $clientId, $deviceName, $clientIp)
            """;
        cmd.Parameters.AddWithValue("$occurredAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$description", description);
        cmd.Parameters.AddWithValue("$clientId", (object?)clientId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deviceName", (object?)deviceName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$clientIp", (object?)clientIp ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Самые новые записи первыми — для отображения в UI.</summary>
    public IReadOnlyList<TaskLogEntry> ListRecent(int limit = 200)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, OccurredAtUtc, Kind, Description, ClientId, DeviceName, ClientIp FROM TaskLog ORDER BY Id DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadAll(cmd);
    }

    /// <summary>Записи новее указанного Id, по возрастанию — для поллинга новых событий (уведомления).</summary>
    public IReadOnlyList<TaskLogEntry> ListSince(int afterId)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, OccurredAtUtc, Kind, Description, ClientId, DeviceName, ClientIp FROM TaskLog WHERE Id > $afterId ORDER BY Id ASC";
        cmd.Parameters.AddWithValue("$afterId", afterId);
        return ReadAll(cmd);
    }

    public int GetMaxId()
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) FROM TaskLog";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<TaskLogEntry> ReadAll(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var result = new List<TaskLogEntry>();
        while (reader.Read())
        {
            result.Add(new TaskLogEntry(
                Id: reader.GetInt32(0),
                OccurredAtUtc: DateTime.Parse(reader.GetString(1)).ToUniversalTime(),
                Kind: reader.GetString(2),
                Description: reader.GetString(3),
                ClientId: reader.IsDBNull(4) ? null : reader.GetString(4),
                DeviceName: reader.IsDBNull(5) ? null : reader.GetString(5),
                ClientIp: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return result;
    }
}
