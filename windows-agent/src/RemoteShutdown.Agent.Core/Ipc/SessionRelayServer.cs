using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace RemoteShutdown.Agent.Core.Ipc;

/// <summary>
/// Именованный канал, которым Service (Session 0, может работать без залогиненного
/// пользователя) достучивается до Tray в активной интерактивной сессии — единственный
/// способ выполнить действие, которое физически требует рабочего стола (см.
/// RelayKinds). Слушает переподключения: Tray может перезапуститься (перелогин
/// пользователя, ручной перезапуск трея) — тогда просто ждём нового клиента.
///
/// Поддерживается один активный Tray одновременно (одна интерактивная сессия на ПК —
/// достаточно для этого проекта; см. docs/roadmap.md).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionRelayServer : IDisposable
{
    public const string PipeName = "RemoteShutdownAgent.SessionRelay";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RelayResponse>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private volatile StreamWriter? _activeWriter;

    /// <param name="pipeName">Не задавайте в проде — только для юнит-тестов, чтобы не
    /// столкнуться с реальным именем канала, если на машине, где гоняются тесты,
    /// случайно запущена настоящая служба агента.</param>
    public SessionRelayServer(string pipeName = PipeName)
    {
        _pipeName = pipeName;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>true, если прямо сейчас есть подключённый Tray — используется только
    /// для диагностики (например, GET /status), не для решения "отправлять или нет":
    /// SendAsync сам вернёт NoActiveSession, если отправить некому.</summary>
    public bool HasActiveSession => _activeWriter is not null;

    public async Task<RelayResponse> SendAsync(RelayRequest request, CancellationToken ct = default)
    {
        var writer = _activeWriter;
        if (writer is null)
            return RelayResponse.Failure(request.Id, RelayErrorCodes.NoActiveSession);

        var tcs = new TaskCompletionSource<RelayResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;

        try
        {
            var line = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(line.AsMemory(), ct);
            await writer.FlushAsync(ct);
        }
        catch
        {
            // Клиент отвалился ровно в момент записи — тот же исход, что и "нет сессии".
            _pending.TryRemove(request.Id, out _);
            return RelayResponse.Failure(request.Id, RelayErrorCodes.NoActiveSession);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultTimeout);
        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return RelayResponse.Failure(request.Id, "Timed out waiting for Tray response.");
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServerStream();
                await server.WaitForConnectionAsync(ct);
                await HandleClientAsync(server, ct);
            }
            catch (OperationCanceledException)
            {
                // Остановка сервиса — выходим тихо.
            }
            catch
            {
                // Клиент отвалился/канал сломался — просто пересоздаём и ждём следующего
                // подключения; Tray сам переподключается с интервалом (см. SessionRelayClient).
            }
            finally
            {
                _activeWriter = null;
                server?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server);
        using var writer = new StreamWriter(server) { AutoFlush = false };
        _activeWriter = writer;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break; // клиент закрыл соединение

            RelayResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<RelayResponse>(line);
            }
            catch
            {
                continue; // мусор в канале — не валим весь relay из-за одной строки
            }

            if (response is not null && _pending.TryRemove(response.Id, out var tcs))
                tcs.TrySetResult(response);
        }
    }

    /// <summary>
    /// Explicit PipeSecurity — сервис работает от LocalSystem, а Tray подключается от
    /// имени обычного залогиненного пользователя; без явного разрешения для
    /// "прошедших проверку пользователей" .NET/Windows по умолчанию не пустит их
    /// подключиться к трубе, созданной LocalSystem-процессом.
    /// </summary>
    private NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            pipeSecurity: security);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* останавливаемся, не критично */ }
        _cts.Dispose();
    }
}
