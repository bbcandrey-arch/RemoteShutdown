using Microsoft.Data.Sqlite;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Core.Tests.Security;

public class PairingServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"agent-tests-{Guid.NewGuid():N}.db");
    private readonly PairingService _pairing;
    private readonly PairedDeviceStore _devices;

    public PairingServiceTests()
    {
        var db = new AgentDatabase(_dbPath);
        db.EnsureCreated();
        var settings = new SettingsStore(db);
        settings.Set(SettingsStore.Keys.PinHash, PinHasher.Hash("123456"));
        _devices = new PairedDeviceStore(db);
        _pairing = new PairingService(settings, _devices);
    }

    [Fact]
    public void Confirm_with_the_correct_pin_pairs_the_device_and_persists_it()
    {
        var session = _pairing.InitPairing("Test Phone");

        var outcome = _pairing.ConfirmPairing(session.PairingSessionId, "123456");

        Assert.Equal(PairConfirmResult.Success, outcome.Result);
        Assert.NotNull(outcome.Device);
        Assert.Equal(32, outcome.Device!.SharedSecret.Length);
        Assert.NotNull(_devices.Find(outcome.Device.ClientId));
    }

    [Fact]
    public void Confirm_with_the_wrong_pin_fails_and_does_not_pair()
    {
        var session = _pairing.InitPairing("Test Phone");

        var outcome = _pairing.ConfirmPairing(session.PairingSessionId, "000000");

        Assert.Equal(PairConfirmResult.InvalidPin, outcome.Result);
        Assert.Empty(_devices.ListAll());
    }

    [Fact]
    public void Confirm_with_an_unknown_session_id_fails()
    {
        var outcome = _pairing.ConfirmPairing(Guid.NewGuid().ToString(), "123456");

        Assert.Equal(PairConfirmResult.SessionNotFound, outcome.Result);
    }

    [Fact]
    public void Five_wrong_attempts_lock_the_session()
    {
        var session = _pairing.InitPairing("Test Phone");

        PairConfirmOutcome outcome = null!;
        for (var i = 0; i < 5; i++)
            outcome = _pairing.ConfirmPairing(session.PairingSessionId, "000000");

        Assert.Equal(PairConfirmResult.Locked, outcome.Result);

        // Even the correct PIN is refused while locked.
        var stillLocked = _pairing.ConfirmPairing(session.PairingSessionId, "123456");
        Assert.Equal(PairConfirmResult.Locked, stillLocked.Result);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
