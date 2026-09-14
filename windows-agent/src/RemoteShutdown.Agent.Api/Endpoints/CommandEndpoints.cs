using RemoteShutdown.Agent.Core.Ipc;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;
using RemoteShutdown.Agent.Core.Timers;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class CommandEndpoints
{
    public sealed record DelayedActionRequest(int DelaySeconds = 0);
    public sealed record VolumeRequest(string Action);
    public sealed record MediaRequest(string Action);

    public static void MapCommandEndpoints(this WebApplication app)
    {
        app.MapPost("/commands/shutdown", (DelayedActionRequest request, HttpContext ctx, PowerActionsService power, TaskLogStore taskLog, TimerSchedulerService timers) =>
            RunPowerAction(ctx, power, taskLog, timers, PowerAction.Shutdown, request.DelaySeconds));

        app.MapPost("/commands/restart", (DelayedActionRequest request, HttpContext ctx, PowerActionsService power, TaskLogStore taskLog, TimerSchedulerService timers) =>
            RunPowerAction(ctx, power, taskLog, timers, PowerAction.Restart, request.DelaySeconds));

        app.MapPost("/commands/sleep", (HttpContext ctx, PowerActionsService power, TaskLogStore taskLog, TimerSchedulerService timers) =>
            RunPowerAction(ctx, power, taskLog, timers, PowerAction.Sleep, 0));

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
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.HibernateNotSupported, ex.Message), statusCode: 409, options: ApiJson.Options);
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, ex.Message), statusCode: 500, options: ApiJson.Options);
            }
        });

        // Lock/Volume/Media физически требуют интерактивного рабочего стола — Service
        // (Session 0, может работать без залогиненного пользователя) сам их выполнить не
        // может и просит Tray сделать это через SessionRelayServer. См. docs/roadmap.md,
        // "Windows Service", и RemoteShutdown.Agent.Core.Ipc.
        app.MapPost("/commands/lock", async (HttpContext ctx, SessionRelayServer relay) =>
        {
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.Lock));
            return RelayResult(ctx, response);
        });

        app.MapPost("/commands/volume", async (VolumeRequest request, HttpContext ctx, SessionRelayServer relay) =>
        {
            // Громкость в журнал/уведомления не пишем — слишком "шумное" событие для
            // журнала задач (пользователь может нажать её десяток раз подряд).
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.Volume, new() { ["action"] = request.Action }));
            return RelayResult(ctx, response);
        });

        app.MapPost("/commands/media", async (MediaRequest request, HttpContext ctx, SessionRelayServer relay) =>
        {
            // Как и громкость — не пишем в журнал задач, слишком частое/некритичное событие.
            var response = await relay.SendAsync(RelayRequest.Create(RelayKinds.Media, new() { ["action"] = request.Action }));
            return RelayResult(ctx, response);
        });
    }

    /// <summary>Единая обработка ответа relay — NoActiveSession отдаём отдельным кодом
    /// ошибки (телефон может показать понятное "Никто не вошёл в систему на ПК"), любую
    /// другую неудачу — как обычную internal error.</summary>
    internal static IResult RelayResult(HttpContext ctx, RelayResponse response)
    {
        var requestId = ctx.GetRequestId();
        if (response.Ok) return Results.Ok(ApiResponse.Ok(requestId));

        var statusCode = response.Error == ErrorCodes.NoActiveSession ? 409 : 500;
        return Results.Json(ApiResponse.Fail(requestId, response.Error ?? ErrorCodes.InternalError, ErrorMessage(response.Error)), statusCode: statusCode, options: ApiJson.Options);
    }

    private static string ErrorMessage(string? code) => code == ErrorCodes.NoActiveSession
        ? "На ПК никто не вошёл в систему — эта команда требует активной сессии."
        : "Не удалось выполнить команду на ПК.";

    private static IResult RunPowerAction(HttpContext ctx, PowerActionsService power, TaskLogStore taskLog, TimerSchedulerService timers, PowerAction action, int delaySeconds)
    {
        try
        {
            power.Execute(action, delaySeconds);
            LogCommand(ctx, taskLog, action, delaySeconds);

            // ПК прямо сейчас по-настоящему выключается/перезагружается (ручной командой,
            // не в тестовом режиме-заглушке) — любой ещё не сработавший таймер из "прошлой
            // жизни" агента отменяем, иначе он воскреснет и неожиданно выполнится сам при
            // следующей загрузке (см. TimerSchedulerService.CancelAllPendingForRealPowerChange —
            // баг-репорт: таймер, оставшийся Pending после ручного выключения раньше срока).
            if (!power.SimulateDangerousActions && (action == PowerAction.Shutdown || action == PowerAction.Restart))
                timers.CancelAllPendingForRealPowerChange();

            var data = delaySeconds > 0
                ? new { scheduledAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds) }
                : null as object;
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), data));
        }
        catch (Exception ex)
        {
            return Results.Json(ApiResponse.Fail(ctx.GetRequestId(), ErrorCodes.InternalError, ex.Message), statusCode: 500, options: ApiJson.Options);
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
