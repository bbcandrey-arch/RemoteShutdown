using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Timers;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class TimerEndpoints
{
    public sealed record CreateTimerRequest(string Action, int? DelaySeconds, DateTime? ScheduledAtUtc);
    public sealed record PatchTimerRequest(string Action, DateTime? ScheduledAtUtc, int? Minutes);

    public static void MapTimerEndpoints(this WebApplication app)
    {
        app.MapPost("/timers", (CreateTimerRequest request, HttpContext ctx, TimerSchedulerService scheduler) =>
        {
            if (!Enum.TryParse<ScheduledAction>(request.Action, ignoreCase: true, out var action))
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, $"Unknown timer action '{request.Action}'."), statusCode: 400);

            var scheduledAtUtc = request.ScheduledAtUtc
                ?? (request.DelaySeconds is { } delay ? DateTime.UtcNow.AddSeconds(delay) : (DateTime?)null);
            if (scheduledAtUtc is null)
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, "Provide delaySeconds or scheduledAtUtc."), statusCode: 400);

            var device = (PairedDevice)ctx.Items["Device"]!;
            var timer = scheduler.Create(action, scheduledAtUtc.Value, device.ClientId);
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), new { timerId = timer.TimerId, scheduledAtUtc = timer.ScheduledAtUtc }));
        });

        app.MapGet("/timers", (HttpContext ctx, TimerSchedulerService scheduler) =>
        {
            var timers = scheduler.ListAll().Select(t => new
            {
                timerId = t.TimerId,
                action = t.Action.ToString(),
                scheduledAtUtc = t.ScheduledAtUtc,
                status = t.Status.ToString(),
            });
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), new { timers }));
        });

        app.MapPatch("/timers/{id}", (string id, PatchTimerRequest request, HttpContext ctx, TimerSchedulerService scheduler) =>
        {
            var requestId = ctx.GetRequestId();
            var updated = request.Action.ToLowerInvariant() switch
            {
                "cancel" => scheduler.Cancel(id),
                "reschedule" when request.ScheduledAtUtc is { } at => scheduler.Reschedule(id, at),
                "snooze" when request.Minutes is { } minutes => scheduler.Snooze(id, minutes),
                _ => null,
            };

            if (updated is null)
                return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.TimerNotFound, $"Timer '{id}' not found or action invalid."), statusCode: 404);

            return Results.Ok(ApiResponse.Ok(requestId, new
            {
                timerId = updated.TimerId,
                scheduledAtUtc = updated.ScheduledAtUtc,
                status = updated.Status.ToString(),
            }));
        });
    }
}
