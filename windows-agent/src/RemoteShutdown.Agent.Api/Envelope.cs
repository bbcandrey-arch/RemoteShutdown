namespace RemoteShutdown.Agent.Api;

/// <summary>Response envelope from docs/protocol.md §3.</summary>
public sealed class ApiError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

public sealed class ApiResponse
{
    public required string RequestId { get; init; }
    public required string Status { get; init; }
    public object? Data { get; init; }
    public ApiError? Error { get; init; }

    public static ApiResponse Ok(string requestId, object? data = null) =>
        new() { RequestId = requestId, Status = "ok", Data = data };

    public static ApiResponse Fail(string requestId, string code, string message) =>
        new() { RequestId = requestId, Status = "error", Error = new ApiError { Code = code, Message = message } };
}

public static class ErrorCodes
{
    public const string InvalidPin = "INVALID_PIN";
    public const string PinLocked = "PIN_LOCKED";
    public const string InvalidSignature = "INVALID_SIGNATURE";
    public const string StaleRequest = "STALE_REQUEST";
    public const string ReplayDetected = "REPLAY_DETECTED";
    public const string UnknownClient = "UNKNOWN_CLIENT";
    public const string TimerNotFound = "TIMER_NOT_FOUND";
    public const string HibernateNotSupported = "HIBERNATE_NOT_SUPPORTED";
    public const string SessionNotFound = "SESSION_NOT_FOUND";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string InternalError = "INTERNAL_ERROR";
    // Команда требует активной интерактивной сессии на ПК (громкость/медиа/блокировка/
    // тачпад) — Service поднялась, но в неё ещё никто не вошёл, либо Tray не подключён
    // к relay. См. RemoteShutdown.Agent.Core.Ipc.SessionRelayServer.
    public const string NoActiveSession = RemoteShutdown.Agent.Core.Ipc.RelayErrorCodes.NoActiveSession;
}
