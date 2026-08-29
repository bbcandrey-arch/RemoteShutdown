using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Volume;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class CommandEndpoints
{
    public sealed record DelayedActionRequest(int DelaySeconds = 0);
    public sealed record VolumeRequest(string Action);

    public static void MapCommandEndpoints(this WebApplication app)
    {
        app.MapPost("/commands/shutdown", (DelayedActionRequest request, HttpContext ctx, PowerActionsService power) =>
            RunPowerAction(ctx, power, PowerAction.Shutdown, request.DelaySeconds));

        app.MapPost("/commands/restart", (DelayedActionRequest request, HttpContext ctx, PowerActionsService power) =>
            RunPowerAction(ctx, power, PowerAction.Restart, request.DelaySeconds));

        app.MapPost("/commands/sleep", (HttpContext ctx, PowerActionsService power) =>
            RunPowerAction(ctx, power, PowerAction.Sleep, 0));

        app.MapPost("/commands/hibernate", (HttpContext ctx, PowerActionsService power) =>
        {
            try
            {
                power.Execute(PowerAction.Hibernate);
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

        app.MapPost("/commands/lock", (HttpContext ctx, PowerActionsService power) =>
            RunPowerAction(ctx, power, PowerAction.Lock, 0));

        app.MapPost("/commands/volume", (VolumeRequest request, HttpContext ctx, VolumeControlService volume) =>
        {
            if (!Enum.TryParse<VolumeAction>(request.Action, ignoreCase: true, out var action))
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, $"Unknown volume action '{request.Action}'."), statusCode: 400);

            volume.Execute(action);
            var state = volume.GetState();
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), new { level = state.Level, muted = state.Muted }));
        });
    }

    private static IResult RunPowerAction(HttpContext ctx, PowerActionsService power, PowerAction action, int delaySeconds)
    {
        try
        {
            power.Execute(action, delaySeconds);
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
}
