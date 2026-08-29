namespace RemoteShutdown.Agent.Core.Storage;

/// <summary>Simple key/value store for agent-wide settings (PIN hash, cert thumbprint, port, ...).</summary>
public sealed class SettingsStore
{
    private readonly AgentDatabase _db;

    public SettingsStore(AgentDatabase db) => _db = db;

    public string? Get(string key)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    public static class Keys
    {
        public const string PinHash = "pin_hash";
        public const string Port = "port";
        public const string CertThumbprint = "cert_thumbprint";
    }
}
