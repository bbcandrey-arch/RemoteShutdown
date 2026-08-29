using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RemoteShutdown.Agent.Core.Events;

namespace RemoteShutdown.Agent.Api.Events;

/// <summary>
/// Implements IAgentEventPublisher by fanning out to every connected WS /events client
/// (docs/protocol.md §3/§8). One phone typically holds one connection, but nothing here
/// assumes that.
/// </summary>
public sealed class WebSocketEventHub : IAgentEventPublisher
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _sockets[id] = socket;

        var buffer = new byte[4096];
        try
        {
            // We don't expect inbound messages, just keep the connection alive until the
            // client disconnects or the server shuts down.
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _sockets.TryRemove(id, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* best effort */ }
            }
        }
    }

    public async Task PublishAsync(string eventName, object data, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { type = "event", @event = eventName, data });
        var bytes = Encoding.UTF8.GetBytes(payload);

        foreach (var (id, socket) in _sockets)
        {
            if (socket.State != WebSocketState.Open)
            {
                _sockets.TryRemove(id, out _);
                continue;
            }

            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            catch (WebSocketException)
            {
                _sockets.TryRemove(id, out _);
            }
        }
    }
}
