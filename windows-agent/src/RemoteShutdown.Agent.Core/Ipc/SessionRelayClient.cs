using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text.Json;

namespace RemoteShutdown.Agent.Core.Ipc;

/// <summary>
/// Клиентская сторона relay (см. SessionRelayServer) — живёт в Tray, внутри
/// интерактивной сессии пользователя. Держит соединение с Service и переподключается
/// сам, если оно рвётся (перезапуск службы, перелогин и т.п.) — вызывающему коду
/// (TrayApplicationContext) достаточно передать один обработчик запросов и забыть.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionRelayClient : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly Func<RelayRequest, Task<RelayResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    /// <param name="pipeName">Не задавайте в проде — см. аналогичный параметр
    /// SessionRelayServer.</param>
    public SessionRelayClient(Func<RelayRequest, Task<RelayResponse>> handler, string pipeName = SessionRelayServer.PipeName)
    {
        _handler = handler;
        _pipeName = pipeName;
        _loop = Task.Run(() => ConnectLoopAsync(_cts.Token));
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, ct);

                using var reader = new StreamReader(client);
                using var writer = new StreamWriter(client) { AutoFlush = false };

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break; // Service закрыл канал (перезапуск/остановка)

                    RelayRequest? request;
                    try { request = JsonSerializer.Deserialize<RelayRequest>(line); }
                    catch { continue; } // мусор в канале — не рвём соединение из-за одной строки

                    if (request is null) continue;

                    RelayResponse response;
                    try { response = await _handler(request); }
                    catch (Exception ex) { response = RelayResponse.Failure(request.Id, ex.Message); }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), ct);
                    await writer.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break; // Dispose() попросил остановиться
            }
            catch
            {
                // Service ещё не поднялся, или канал порвался — подождать и попробовать снова.
            }

            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(ReconnectDelay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* останавливаемся, не критично */ }
        _cts.Dispose();
    }
}
