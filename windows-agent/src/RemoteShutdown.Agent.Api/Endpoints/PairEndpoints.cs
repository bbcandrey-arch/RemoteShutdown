using RemoteShutdown.Agent.Core.Network;
using RemoteShutdown.Agent.Core.Security;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class PairEndpoints
{
    public sealed record PairInitRequest(string DeviceName);
    public sealed record PairConfirmRequest(string PairingSessionId, string Pin);

    public static void MapPairEndpoints(this WebApplication app)
    {
        app.MapPost("/pair/init", (PairInitRequest request, HttpContext ctx, PairingService pairing) =>
        {
            var requestId = ctx.GetRequestId();
            var session = pairing.InitPairing(request.DeviceName);
            return Results.Ok(ApiResponse.Ok(requestId, new { pairingSessionId = session.PairingSessionId }));
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
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.InvalidPin, "Incorrect PIN."), statusCode: 401);

                case PairConfirmResult.Locked:
                    var seconds = (int)Math.Ceiling((outcome.LockRemaining ?? TimeSpan.Zero).TotalSeconds);
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.PinLocked, $"Too many attempts. Try again in {seconds}s."), statusCode: 429);

                case PairConfirmResult.SessionExpired:
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.SessionExpired, "Pairing session expired."), statusCode: 410);

                default:
                    return Results.Json(ApiResponse.Fail(requestId, ErrorCodes.SessionNotFound, "Unknown pairing session."), statusCode: 404);
            }
        });
    }
}
