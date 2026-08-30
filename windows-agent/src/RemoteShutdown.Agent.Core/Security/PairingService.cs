using System.Collections.Concurrent;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Core.Security;

/// <summary>
/// Implements the pairing flow from docs/protocol.md §4: an unsigned pair/init
/// session, PIN verification with rate-limiting/lockout, then a signed pair/confirm
/// response handing the phone its clientId + sharedSecret.
/// </summary>
public sealed class PairingService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);
    private const int MaxAttemptsBeforeLock = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);

    private readonly SettingsStore _settings;
    private readonly PairedDeviceStore _devices;

    // In-memory: pairing sessions are short-lived and don't need to survive a restart.
    private readonly ConcurrentDictionary<string, PairingSession> _sessions = new();
    private readonly ConcurrentDictionary<string, (int Attempts, DateTime? LockedUntilUtc)> _attempts = new();

    public PairingService(SettingsStore settings, PairedDeviceStore devices)
    {
        _settings = settings;
        _devices = devices;
    }

    public PairingSession InitPairing(string deviceName, string? platform = null, string? model = null)
    {
        var session = new PairingSession(Guid.NewGuid().ToString(), deviceName, DateTime.UtcNow, platform, model);
        _sessions[session.PairingSessionId] = session;
        PruneExpiredSessions();
        return session;
    }

    public PairConfirmOutcome ConfirmPairing(string pairingSessionId, string pin)
    {
        if (!_sessions.TryGetValue(pairingSessionId, out var session))
            return new PairConfirmOutcome(PairConfirmResult.SessionNotFound);

        if (DateTime.UtcNow - session.CreatedAtUtc > SessionTtl)
        {
            _sessions.TryRemove(pairingSessionId, out _);
            return new PairConfirmOutcome(PairConfirmResult.SessionExpired);
        }

        var state = _attempts.GetValueOrDefault(pairingSessionId, (0, null));
        if (state.LockedUntilUtc is { } lockedUntil && lockedUntil > DateTime.UtcNow)
            return new PairConfirmOutcome(PairConfirmResult.Locked, LockRemaining: lockedUntil - DateTime.UtcNow);

        var pinHash = _settings.Get(SettingsStore.Keys.PinHash);
        if (pinHash is null || !PinHasher.Verify(pin, pinHash))
        {
            var attempts = state.Attempts + 1;
            DateTime? lockedUntilUtc = attempts >= MaxAttemptsBeforeLock ? DateTime.UtcNow + LockDuration : null;
            _attempts[pairingSessionId] = (lockedUntilUtc is not null ? 0 : attempts, lockedUntilUtc);

            return lockedUntilUtc is not null
                ? new PairConfirmOutcome(PairConfirmResult.Locked, LockRemaining: LockDuration)
                : new PairConfirmOutcome(PairConfirmResult.InvalidPin);
        }

        _sessions.TryRemove(pairingSessionId, out _);
        _attempts.TryRemove(pairingSessionId, out _);

        var device = new PairedDevice(
            ClientId: Guid.NewGuid().ToString(),
            DeviceName: session.DeviceName,
            SharedSecret: HmacSigner.GenerateSharedSecret(),
            PairedAtUtc: DateTime.UtcNow,
            LastSeenUtc: null,
            Revoked: false,
            Platform: session.Platform,
            Model: session.Model);

        _devices.Add(device);
        return new PairConfirmOutcome(PairConfirmResult.Success, device);
    }

    private void PruneExpiredSessions()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;
        foreach (var (id, session) in _sessions)
        {
            if (session.CreatedAtUtc < cutoff)
                _sessions.TryRemove(id, out _);
        }
    }
}
