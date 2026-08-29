using Microsoft.Data.Sqlite;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Core.Security;

/// <summary>
/// SQLite-backed repository for paired devices. Shared secrets are stored DPAPI-protected
/// (see DpapiProtector) and only decrypted in memory when needed to verify a signature.
/// </summary>
public sealed class PairedDeviceStore
{
    private readonly AgentDatabase _db;

    public PairedDeviceStore(AgentDatabase db) => _db = db;

    public void Add(PairedDevice device)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PairedDevices (ClientId, DeviceName, SharedSecretProtected, PairedAtUtc, LastSeenUtc, Revoked)
            VALUES ($clientId, $deviceName, $secret, $pairedAt, $lastSeen, 0)
            """;
        cmd.Parameters.AddWithValue("$clientId", device.ClientId);
        cmd.Parameters.AddWithValue("$deviceName", device.DeviceName);
        cmd.Parameters.AddWithValue("$secret", DpapiProtector.Protect(device.SharedSecret));
        cmd.Parameters.AddWithValue("$pairedAt", device.PairedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$lastSeen", (object?)device.LastSeenUtc?.ToString("O") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public PairedDevice? Find(string clientId)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ClientId, DeviceName, SharedSecretProtected, PairedAtUtc, LastSeenUtc, Revoked FROM PairedDevices WHERE ClientId = $clientId";
        cmd.Parameters.AddWithValue("$clientId", clientId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return Map(reader);
    }

    public IReadOnlyList<PairedDevice> ListAll()
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ClientId, DeviceName, SharedSecretProtected, PairedAtUtc, LastSeenUtc, Revoked FROM PairedDevices ORDER BY PairedAtUtc DESC";

        using var reader = cmd.ExecuteReader();
        var result = new List<PairedDevice>();
        while (reader.Read())
            result.Add(Map(reader));
        return result;
    }

    public void UpdateLastSeen(string clientId, DateTime utcNow)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE PairedDevices SET LastSeenUtc = $lastSeen WHERE ClientId = $clientId";
        cmd.Parameters.AddWithValue("$lastSeen", utcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.ExecuteNonQuery();
    }

    public void Revoke(string clientId)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE PairedDevices SET Revoked = 1 WHERE ClientId = $clientId";
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.ExecuteNonQuery();
    }

    private static PairedDevice Map(SqliteDataReader reader) => new(
        ClientId: reader.GetString(0),
        DeviceName: reader.GetString(1),
        SharedSecret: DpapiProtector.Unprotect((byte[])reader[2]),
        PairedAtUtc: DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
        LastSeenUtc: reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)).ToUniversalTime(),
        Revoked: reader.GetInt32(5) != 0);
}
