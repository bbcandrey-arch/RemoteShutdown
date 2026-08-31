using Microsoft.Data.Sqlite;

namespace RemoteShutdown.Agent.Core.Storage;

/// <summary>
/// Owns the SQLite connection string and schema for the agent's local state
/// (paired devices, timers, settings). See docs/protocol.md for the wire format
/// these tables back.
/// </summary>
public sealed class AgentDatabase
{
    public string DbPath { get; }
    public string ConnectionString { get; }

    public AgentDatabase(string? dbPath = null)
    {
        DbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RemoteShutdownAgent",
            "agent.db");

        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        ConnectionString = $"Data Source={DbPath}";
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS PairedDevices (
                    ClientId TEXT PRIMARY KEY,
                    DeviceName TEXT NOT NULL,
                    SharedSecretProtected BLOB NOT NULL,
                    PairedAtUtc TEXT NOT NULL,
                    LastSeenUtc TEXT NULL,
                    Revoked INTEGER NOT NULL DEFAULT 0,
                    Platform TEXT NULL,
                    Model TEXT NULL,
                    AppVersion TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS Timers (
                    TimerId TEXT PRIMARY KEY,
                    Action TEXT NOT NULL,
                    ScheduledAtUtc TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedByClientId TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                -- Журнал прилетевших с телефона задач (команды питания, создание/отмена
                -- таймеров, срабатывание таймера) — источник и для уведомлений в трее
                -- (TaskLogWatcher), и для вкладки "Журнал" в настройках. См. docs/roadmap.md.
                CREATE TABLE IF NOT EXISTS TaskLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OccurredAtUtc TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    ClientId TEXT NULL,
                    DeviceName TEXT NULL,
                    ClientIp TEXT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // Миграции для БД, созданных до появления этих колонок — CREATE TABLE IF NOT
        // EXISTS выше их не добавит на уже существующей таблице.
        AddColumnIfMissing(connection, "PairedDevices", "Platform", "TEXT NULL");
        AddColumnIfMissing(connection, "PairedDevices", "Model", "TEXT NULL");
        AddColumnIfMissing(connection, "PairedDevices", "AppVersion", "TEXT NULL");
        AddColumnIfMissing(connection, "TaskLog", "ClientIp", "TEXT NULL");
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string columnDefinition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return; // колонка уже есть — ничего делать не нужно
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition}";
        alter.ExecuteNonQuery();
    }
}
