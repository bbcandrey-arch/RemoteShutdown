namespace RemoteShutdown.Agent.Core.Security;

public sealed record PairedDevice(
    string ClientId,
    string DeviceName,
    byte[] SharedSecret,
    DateTime PairedAtUtc,
    DateTime? LastSeenUtc,
    bool Revoked);

public sealed record PairingSession(string PairingSessionId, string DeviceName, DateTime CreatedAtUtc);

public enum PairConfirmResult
{
    Success,
    InvalidPin,
    Locked,
    SessionNotFound,
    SessionExpired,
}

public sealed record PairConfirmOutcome(
    PairConfirmResult Result,
    PairedDevice? Device = null,
    TimeSpan? LockRemaining = null);
