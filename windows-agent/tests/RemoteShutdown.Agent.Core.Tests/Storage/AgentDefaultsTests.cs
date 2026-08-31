using Microsoft.Data.Sqlite;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;
using Xunit;

namespace RemoteShutdown.Agent.Core.Tests.Storage;

/// <summary>
/// Значения по умолчанию для новой БД (docs/security.md) — намеренно на временной БД
/// (не реальном agent.db), т.к. один из проверяемых здесь фактов — что test_mode по
/// умолчанию ВЫКЛЮЧЕН для незнакомой БД, и тест не должен рисковать этим значением на
/// настоящей машине.
/// </summary>
public class AgentDefaultsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"agent-defaults-test-{Guid.NewGuid():N}.db");

    private SettingsStore CreateSettings()
    {
        var db = new AgentDatabase(_dbPath);
        db.EnsureCreated();
        return new SettingsStore(db);
    }

    [Fact]
    public void Apply_OnFreshDatabase_LeavesTestModeDisabled()
    {
        var settings = CreateSettings();

        AgentDefaults.Apply(settings);

        // Тестовый режим — боевой инструмент выключения ПК, не отладочная песочница по
        // умолчанию; включать должен осознанно сам пользователь в настройках.
        Assert.Equal("false", settings.Get(SettingsStore.Keys.TestMode));
    }

    [Fact]
    public void Apply_DoesNotOverwrite_AnExistingTestModeValue()
    {
        var settings = CreateSettings();
        settings.Set(SettingsStore.Keys.TestMode, "true");

        AgentDefaults.Apply(settings);

        Assert.Equal("true", settings.Get(SettingsStore.Keys.TestMode));
    }

    [Fact]
    public void Apply_OnFreshDatabase_GeneratesAWorkingRandomPin()
    {
        var settings = CreateSettings();

        var result = AgentDefaults.Apply(settings);

        Assert.NotNull(result.GeneratedPin);
        Assert.Equal(6, result.GeneratedPin!.Length);
        Assert.True(int.TryParse(result.GeneratedPin, out _));

        var storedHash = settings.Get(SettingsStore.Keys.PinHash);
        Assert.NotNull(storedHash);
        Assert.True(PinHasher.Verify(result.GeneratedPin, storedHash!));
        Assert.NotNull(settings.Get(SettingsStore.Keys.PinPlainProtected));
    }

    [Fact]
    public void Apply_DoesNotOverwrite_AnExistingPin()
    {
        var settings = CreateSettings();
        settings.Set(SettingsStore.Keys.PinHash, PinHasher.Hash("123456"));

        var result = AgentDefaults.Apply(settings);

        Assert.Null(result.GeneratedPin);
        Assert.True(PinHasher.Verify("123456", settings.Get(SettingsStore.Keys.PinHash)!));
    }

    [Fact]
    public void Apply_SetsPortAndDeviceName_WhenMissing()
    {
        var settings = CreateSettings();

        AgentDefaults.Apply(settings);

        Assert.Equal(AgentDefaults.DefaultPort.ToString(), settings.Get(SettingsStore.Keys.Port));
        Assert.False(string.IsNullOrWhiteSpace(settings.Get(SettingsStore.Keys.DeviceName)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
