using RemoteShutdown.Agent.Core.Network;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class PairEndpoints
{
    // Platform/Model/AppVersion — необязательные: "Android"/"iOS"/... , модель телефона
    // ("SM-S908E", "Pixel 7", ...) и версия мобильного приложения, для отображения на
    // вкладке "Устройства" в трее. Старые клиенты их просто не пришлют — null, ничего
    // не ломается.
    public sealed record PairInitRequest(string DeviceName, string? Platform = null, string? Model = null, string? AppVersion = null);
    public sealed record PairConfirmRequest(string PairingSessionId, string Pin);

    public static void MapPairEndpoints(this WebApplication app)
    {
        app.MapPost("/pair/init", (PairInitRequest request, HttpContext ctx, PairingService pairing, SettingsStore settings) =>
        {
            var requestId = ctx.GetRequestId();
            var session = pairing.InitPairing(request.DeviceName, request.Platform, request.Model, request.AppVersion);
            // agentName — имя самого ПК (не путать с request.DeviceName, это имя телефона,
            // которое агент показывает в списке сопряжённых устройств). Телефон использует
            // agentName как подпись этого ПК — см. docs/roadmap.md, "переименование ПК" и
            // "несколько агентов". Возвращается независимо от того, пришёл ли пейринг через
            // QR (там имя тоже может быть, но /pair/init — источник истины) или вручную по IP.
            var agentName = settings.Get(SettingsStore.Keys.DeviceName) ?? Environment.MachineName;
            return Results.Ok(ApiResponse.Ok(requestId, new { pairingSessionId = session.PairingSessionId, agentName }));
        });

        app.MapPost("/pair/confirm", (PairConfirmRequest request, HttpContext ctx, PairingService pairing) =>
        {
            var requestId = ctx.GetRequestId();
            var outcome = pairing.ConfirmPairing(request.PairingSessionId, request.Pin);

            switch (outcome.Result)
            {
                case PairConfirmResult.Success:
                    var device = outcome.Device!;
                    return Results.Ok(ApiResponse.Ok(requestId, new
                    {
                        clientId = device.ClientId,
                        sharedSecret = Convert.ToBase64String(device.SharedSecret),
                        deviceMac = NetworkInfoService.GetPrimaryMacAddress(),
                        broadcastHint = NetworkInfoService.GetBroadcastHint(),
                    }));

                case PairConfirmResult.InvalidPin:
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.InvalidPin, "Incorrect PIN."), statusCode: 401, options: ApiJson.Options);

                case PairConfirmResult.Locked:
                    var seconds = (int)Math.Ceiling((outcome.LockRemaining ?? TimeSpan.Zero).TotalSeconds);
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.PinLocked, $"Too many attempts. Try again in {seconds}s."), statusCode: 429, options: ApiJson.Options);

                case PairConfirmResult.SessionExpired:
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.SessionExpired, "Pairing session expired."), statusCode: 410, options: ApiJson.Options);

                default:
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.SessionNotFound, "Unknown pairing session."), statusCode: 404, options: ApiJson.Options);
            }
        });
    }
}
