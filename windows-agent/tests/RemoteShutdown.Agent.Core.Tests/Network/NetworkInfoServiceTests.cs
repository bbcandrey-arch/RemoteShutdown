using RemoteShutdown.Agent.Core.Network;
using Xunit;

namespace RemoteShutdown.Agent.Core.Tests.Network;

public class NetworkInfoServiceTests
{
    [Fact]
    public void GetAllIPv4Addresses_ReturnsAtLeastOneUsableEntry()
    {
        // Тестовая машина всегда на какой-то сети (Wi-Fi/Ethernet) — если список пуст,
        // GetPrimaryIPv4Address (используемый при старте агента для QR-кода) сломан.
        var interfaces = NetworkInfoService.GetAllIPv4Addresses();

        Assert.NotEmpty(interfaces);
        Assert.All(interfaces, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.InterfaceName));
            Assert.False(string.IsNullOrWhiteSpace(i.Address));
        });
    }

    [Fact]
    public void GetPrimaryIPv4Address_MatchesFirstOfGetAllIPv4Addresses()
    {
        var all = NetworkInfoService.GetAllIPv4Addresses();
        var primary = NetworkInfoService.GetPrimaryIPv4Address();

        Assert.Equal(all[0].Address, primary);
    }
}
