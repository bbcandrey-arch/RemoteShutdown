namespace RemoteShutdown.Agent.Core.Events;

/// <summary>
/// Core-side abstraction for pushing WS events (docs/protocol.md §3/§7). The actual
/// WebSocket hub lives in RemoteShutdown.Agent.Api and implements this so Core stays
/// free of ASP.NET Core dependencies.
/// </summary>
public interface IAgentEventPublisher
{
    Task PublishAsync(string eventName, object data, CancellationToken ct = default);
}

/// <summary>No-op used before the API host wires up the real publisher (e.g. in unit tests).</summary>
public sealed class NullAgentEventPublisher : IAgentEventPublisher
{
    public Task PublishAsync(string eventName, object data, CancellationToken ct = default) => Task.CompletedTask;
}
