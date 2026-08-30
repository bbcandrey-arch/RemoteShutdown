using RemoteShutdown.Agent.Core.Media;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;
using RemoteShutdown.Agent.Core.Volume;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class CommandEndpoints
{
    public sealed record DelayedActionRequest(int DelaySeconds = 0);
    public sealed record VolumeRequest(string Action);
    public sealed record MediaRequest(string Action);

    public static void MapCommandEndpoints(this WebApplication app)
    {
        app.MapPost("/commands/shutdown", (DelayedActionRequest request, HttpContext ctx, PowerActionsService power, TaskLogStore taskLog) =>
            RunPowerAction(ctx, power, taskLog, PowerAction.Shutdown, request.DelaySeconds));

        app.MapPost("/commands/restart", (DelayedActionRequest request, HttpContext ctx, PowerActionsService power, TaskLogStore taskLog) =>
            RunPowerAction(ctx, power, taskLog, PowerAction.Restart, request.DelaySeconds));

        app.MapPost("/commands/sleep", (HttpContext ctx, PowerActionsService power, TaskLogStore taskLog) =>
            RunPowerAction(ctx, power, taskLog, PowerAction.Sleep, 0));

        app.MapPost("/commands/hibernate", (HttpContext ctx, PowerActionsService power, TaskLogStore taskLog) =>
        {
            try
            {
                power.Execute(PowerAction.Hibernate);
                LogCommand(ctx, taskLog, PowerAction.Hibernate);
                return Results.Ok(ApiResponse.Ok(ctx.GetRequestId()));
            }
            catch (HibernateNotSupportedException ex)
            {
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.HibernateNotSupported, ex.Message), statusCode: 409);
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, ex.Message), statusCode: 500);
            }
        });

        app.MapPost("/commands/lock", (HttpContext ctx, PowerActionsService power, TaskLogStore taskLog) =>
            RunPowerAction(ctx, power, taskLog, PowerAction.Lock, 0));

        app.MapPost("/commands/volume", (VolumeRequest request, HttpContext ctx, VolumeControlService volume) =>
        {
            if (!Enum.TryParse<VolumeAction>(request.Action, ignoreCase: true, out var action))
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, $"Unknown volume action '{request.Action}'."), statusCode: 400);

            // Громкость в журнал/уведомления не пишем — слишком "шумное" событие для
            // журнала задач (пользователь может нажать её десяток раз подряд).
            volume.Execute(action);
            var state = volume.GetState();
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), new { level = state.Level, muted = state.Muted }));
        });

        app.MapPost("/commands/media", (MediaRequest request, HttpContext ctx, MediaControlService media) =>
        {
            if (!Enum.TryParse<MediaAction>(request.Action, ignoreCase: true, out var action))
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, $"Unknown media action '{request.Action}'."), statusCode: 400);

            // Как и громкость — не пишем в журнал задач, слишком частое/некритичное событие.
            media.Execute(action);
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId()));
        });
    }

    private static IResult RunPowerAction(HttpContext ctx, PowerActionsService power, TaskLogStore taskLog, PowerAction action, int delaySeconds)
    {
        try
        {
            power.Execute(action, delaySeconds);
            LogCommand(ctx, taskLog, action, delaySeconds);
            var data = delaySeconds > 0
                ? new { scheduledAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds) }
                : null as object;
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), data));
        }
        catch (Exception ex)
        {
            return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, ex.Message), statusCode: 500);
        }
    }

    private static void LogCommand(HttpContext ctx, TaskLogStore taskLog, PowerAction action, int delaySeconds = 0)
    {
        var device = ctx.Items["Device"] as PairedDevice;
        var when = delaySeconds > 0 ? $" через {delaySeconds / 60} мин" : " сейчас";
        taskLog.Add("command", $"{ActionLabel(action)}{when}", device?.ClientId, device?.DeviceName, ClientIp(ctx));
    }

    /// <summary>IP, с которого пришла команда — показывается в журнале (docs/roadmap.md),
    /// чтобы можно было заметить команду с незнакомого адреса в своей сети.</summary>
    private static string? ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();

    private static string ActionLabel(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Выключение",
        PowerAction.Restart => "Перезагрузка",
        PowerAction.Sleep => "Сон",
        PowerAction.Hibernate => "Гибернация",
        PowerAction.Lock => "Блокировка экрана",
        _ => action.ToString(),
    };
}
