namespace RemoteShutdown.Agent.Core.Ipc;

/// <summary>
/// Запрос от Service (Session 0, может быть без залогиненного пользователя) к Tray
/// (живёт в интерактивной сессии) — на действия, которые физически требуют доступа к
/// рабочему столу: громкость, медиаклавиши, блокировка экрана, мышь/клавиатура тачпада.
/// См. docs/roadmap.md, "Windows Service (v2)", и PowerActionsService — Shutdown/Restart/
/// Sleep/Hibernate этого не требуют и выполняются в Service напрямую.
/// </summary>
public sealed record RelayRequest(string Id, string Kind, Dictionary<string, string> Params)
{
    public static RelayRequest Create(string kind, Dictionary<string, string>? parameters = null) =>
        new(Guid.NewGuid().ToString(), kind, parameters ?? new Dictionary<string, string>());
}

public sealed record RelayResponse(string Id, bool Ok, string? Error)
{
    public static RelayResponse Success(string id) => new(id, true, null);
    public static RelayResponse Failure(string id, string error) => new(id, false, error);
}

/// <summary>Виды запросов, поддерживаемых relay — держим строками (не enum) в самом
/// протоколе, чтобы будущая версия Tray могла просто не узнать новый Kind и ответить
/// ошибкой, а не упасть на десериализации.</summary>
public static class RelayKinds
{
    public const string Lock = "lock";
    public const string Volume = "volume";
    public const string Media = "media";
    public const string MouseMove = "mouseMove";
    public const string MouseClick = "mouseClick";
    public const string KeyboardText = "keyboardText";
    public const string KeyboardKey = "keyboardKey";
}

/// <summary>Код ошибки API, когда Service не смог достучаться до Tray — либо никто не
/// вошёл в систему на ПК, либо Tray ещё не успел подключиться к каналу.</summary>
public static class RelayErrorCodes
{
    public const string NoActiveSession = "NO_ACTIVE_SESSION";
}
