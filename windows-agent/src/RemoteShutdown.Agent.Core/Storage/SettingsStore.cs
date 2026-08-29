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

        /// <summary>
        /// "true"/"false" — переключается из настроек трей-приложения без перезапуска
        /// агента (PowerActionsService перечитывает это значение при каждом выполнении
        /// команды). См. docs/security.md, "Заглушка опасных действий".
        /// </summary>
        public const string TestMode = "test_mode";

        /// <summary>
        /// DPAPI-защищённая (DpapiProtector.ProtectPin) копия текущего PIN в base64 —
        /// хранится ТОЛЬКО чтобы показать его в окне настроек трея (пользователю нужно
        /// вводить его на телефоне при пейринге). Проверка PIN всегда идёт через PinHash,
        /// эта запись на неё никак не влияет.
        /// </summary>
        public const string PinPlainProtected = "pin_plain_protected";

        /// <summary>
        /// IPv4-адрес, выбранный пользователем в настройках, если на машине несколько
        /// сетевых интерфейсов (Wi-Fi + Ethernet, VPN и т.д.) — иначе агент берёт первый
        /// найденный, что не всегда тот, что нужен. См. docs/roadmap.md.
        /// </summary>
        public const string PreferredIp = "preferred_ip";

        /// <summary>
        /// Имя этого ПК, как оно показывается в приложении на телефоне (заголовок Dashboard,
        /// список сопряжённых ПК при поддержке нескольких агентов). По умолчанию —
        /// Environment.MachineName, редактируется в SettingsForm ("Общие"), зашивается в
        /// QR-код (PairingQrService) и возвращается в ответе /pair/init — так название видно
        /// и при сканировании QR, и при ручном pairing по IP. См. docs/roadmap.md.
        /// </summary>
        public const string DeviceName = "device_name";
    }
}
