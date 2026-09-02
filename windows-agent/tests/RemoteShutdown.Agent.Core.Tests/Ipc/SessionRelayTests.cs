using RemoteShutdown.Agent.Core.Ipc;
using Xunit;

namespace RemoteShutdown.Agent.Core.Tests.Ipc;

/// <summary>
/// Все тесты используют собственное уникальное имя канала (не production
/// SessionRelayServer.PipeName) — чтобы не столкнуться с реальной службой агента,
/// если она случайно запущена на машине, где гоняются тесты.
/// </summary>
public class SessionRelayTests
{
    private static string UniquePipeName() => $"RemoteShutdownAgent.Test.{Guid.NewGuid():N}";

    [Fact]
    public async Task SendAsync_WithNoClientConnected_ReturnsNoActiveSession()
    {
        using var server = new SessionRelayServer(UniquePipeName());

        var response = await server.SendAsync(RelayRequest.Create(RelayKinds.Lock));

        Assert.False(response.Ok);
        Assert.Equal(RelayErrorCodes.NoActiveSession, response.Error);
    }

    [Fact]
    public async Task SendAsync_WithConnectedClient_RoundTripsToHandlerAndBack()
    {
        var pipeName = UniquePipeName();
        using var server = new SessionRelayServer(pipeName);
        using var client = new SessionRelayClient(
            req => Task.FromResult(req.Kind == RelayKinds.Volume
                ? RelayResponse.Success(req.Id)
                : RelayResponse.Failure(req.Id, "unexpected kind")),
            pipeName);

        // Клиент подключается в фоне (см. SessionRelayClient.ConnectLoopAsync) — дать
        // ему немного времени, прежде чем полагаться на HasActiveSession.
        var connected = await WaitUntilAsync(() => server.HasActiveSession, TimeSpan.FromSeconds(5));
        Assert.True(connected, "Клиент не подключился к relay за отведённое время.");

        var response = await server.SendAsync(RelayRequest.Create(RelayKinds.Volume, new() { ["action"] = "up" }));

        Assert.True(response.Ok);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }
}
