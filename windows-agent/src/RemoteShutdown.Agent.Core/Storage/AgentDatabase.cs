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
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS PairedDevices (
                ClientId TEXT PRIMARY KEY,
                DeviceName TEXT NOT NULL,
                SharedSecretProtected BLOB NOT NULL,
                PairedAtUtc TEXT NOT NULL,
                LastSeenUtc TEXT NULL,
                Revoked INTEGER NOT NULL DEFAULT 0
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
            """;
        cmd.ExecuteNonQuery();
    }
}
