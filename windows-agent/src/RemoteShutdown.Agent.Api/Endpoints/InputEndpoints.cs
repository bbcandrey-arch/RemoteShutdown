using RemoteShutdown.Agent.Core.Input;

namespace RemoteShutdown.Agent.Api.Endpoints;

/// <summary>
/// Тачпад с телефона (docs/roadmap.md) — перемещение курсора, клики, текст и спецклавиши.
/// Как и громкость/медиа, в журнал задач не пишем — слишком частые и некритичные события,
/// журнал моментально захлестнёт при активном использовании тачпада.
/// </summary>
public static class InputEndpoints
{
    public sealed record MouseMoveRequest(int Dx, int Dy);
    public sealed record MouseClickRequest(string Button);
    public sealed record KeyboardTextRequest(string Text);
    public sealed record KeyboardKeyRequest(string Key);

    public static void MapInputEndpoints(this WebApplication app)
    {
        app.MapPost("/input/mouse/move", (MouseMoveRequest request, HttpContext ctx, RemoteInputService input) =>
        {
            input.MoveMouse(request.Dx, request.Dy);
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId()));
        });

        app.MapPost("/input/mouse/click", (MouseClickRequest request, HttpContext ctx, RemoteInputService input) =>
        {
            if (!Enum.TryParse<MouseButton>(request.Button, ignoreCase: true, out var button))
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, $"Unknown mouse button '{request.Button}'."), statusCode: 400);

            input.Click(button);
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId()));
        });

        app.MapPost("/input/keyboard/text", (KeyboardTextRequest request, HttpContext ctx, RemoteInputService input) =>
        {
            input.TypeText(request.Text);
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId()));
        });

        app.MapPost("/input/keyboard/key", (KeyboardKeyRequest request, HttpContext ctx, RemoteInputService input) =>
        {
            if (!Enum.TryParse<SpecialKey>(request.Key, ignoreCase: true, out var key))
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, $"Unknown key '{request.Key}'."), statusCode: 400);

            input.SendSpecialKey(key);
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId()));
        });
    }
}
