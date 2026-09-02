using RemoteShutdown.Agent.Core.Ipc;

namespace RemoteShutdown.Agent.Api.Endpoints;

/// <summary>
/// Тачпад с телефона (docs/roadmap.md) — перемещение курсора, клики, текст и спецклавиши.
/// Как и громкость/медиа, в журнал задач не пишем — слишком частые и некритичные события,
/// журнал моментально захлестнёт при активном использовании тачпада.
///
/// Мышь/клавиатура физически действуют на конкретный рабочий стол — Service (Session 0)
/// не может их выполнить сам, только попросить Tray через SessionRelayServer (см.
/// CommandEndpoints.RelayResult — та же обработка ответа, что и для громкости/медиа/
/// блокировки).
/// </summary>
public static class InputEndpoints
{
    public sealed record MouseMoveRequest(int Dx, int Dy);
    public sealed record MouseClickRequest(string Button);
    public sealed record KeyboardTextRequest(string Text);
    public sealed record KeyboardKeyRequest(string Key);

    public static void MapInputEndpoints(this WebApplication app)
    {
        app.MapPost("/input/mouse/move", async (MouseMoveRequest request, HttpContext ctx, SessionRelayServer relay) =>
        {
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.MouseMove,
                new() { ["dx"] = request.Dx.ToString(), ["dy"] = request.Dy.ToString() }));
            return CommandEndpoints.RelayResult(ctx, response);
        });

        app.MapPost("/input/mouse/click", async (MouseClickRequest request, HttpContext ctx, SessionRelayServer relay) =>
        {
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.MouseClick, new() { ["button"] = request.Button }));
            return CommandEndpoints.RelayResult(ctx, response);
        });

        app.MapPost("/input/keyboard/text", async (KeyboardTextRequest request, HttpContext ctx, SessionRelayServer relay) =>
        {
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.KeyboardText, new() { ["text"] = request.Text }));
            return CommandEndpoints.RelayResult(ctx, response);
        });

        app.MapPost("/input/keyboard/key", async (KeyboardKeyRequest request, HttpContext ctx, SessionRelayServer relay) =>
        {
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.KeyboardKey, new() { ["key"] = request.Key }));
            return CommandEndpoints.RelayResult(ctx, response);
        });
    }
}
