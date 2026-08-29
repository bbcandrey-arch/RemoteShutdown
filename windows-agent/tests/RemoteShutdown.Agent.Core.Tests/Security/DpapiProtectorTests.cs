using System.Text;
using RemoteShutdown.Agent.Core.Security;
using Xunit;

namespace RemoteShutdown.Agent.Core.Tests.Security;

/// <summary>
/// Проверяет DPAPI-обёртку для хранения PIN в открытом виде (для показа в окне
/// настроек трея, см. SettingsForm) — отдельная "цель" (entropy) от секретов
/// сопряжённых устройств, см. DpapiProtector.
/// </summary>
public class DpapiProtectorTests
{
    [Fact]
    public void ProtectPin_RoundTrips()
    {
        var pin = Encoding.UTF8.GetBytes("483920");

        var protectedBytes = DpapiProtector.ProtectPin(pin);
        var unprotected = DpapiProtector.UnprotectPin(protectedBytes);

        Assert.Equal(pin, unprotected);
    }

    [Fact]
    public void ProtectPin_UsesDifferentEntropyThanSharedSecretProtect()
    {
        // Блоб, защищённый для PIN, не должен расшифровываться как секрет устройства
        // (и наоборот) — это защита от случайной путаницы данных внутри одной БД.
        var pin = Encoding.UTF8.GetBytes("483920");
        var protectedAsPin = DpapiProtector.ProtectPin(pin);

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => DpapiProtector.Unprotect(protectedAsPin));
    }
}
