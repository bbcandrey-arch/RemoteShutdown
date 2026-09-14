using System.Text;
using System.Text.Json;
using RemoteShutdown.Agent.Core.Security;

namespace RemoteShutdown.Agent.Api.Middleware;

/// <summary>
/// Verifies X-Client-Id/X-Timestamp/X-Nonce/X-Signature per docs/protocol.md §5 for every
/// request except pairing (which has no shared secret yet). On success, stashes the
/// resolved PairedDevice in HttpContext.Items["Device"] for endpoints to use.
/// </summary>
public sealed class HmacAuthMiddleware
{
    private static readonly string[] UnauthenticatedPaths = ["/pair/init", "/pair/confirm"];
    // 3 минуты, а не 30 секунд — баг-репорт: часы ПК без NTP-синхронизации (источник
    // "Local CMOS Clock") незаметно "уплыли" на пару минут от реального времени, и это
    // молча обрывало все подписанные запросы с телефона (правильно синхронизированного)
    // ошибкой "Request timestamp outside the validity window" — без внятной подсказки,
    // в чём дело (см. docs/user-guide.md, "Решение проблем"). От повтора запроса (replay)
    // и так защищает отдельно NonceCache — это окно нужно только как разумная граница
    // "часы вообще не должны так сильно расходиться", а не единственная линия защиты.
    private const long TimestampToleranceMs = 180_000;

    private readonly RequestDelegate _next;

    public HmacAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, PairedDeviceStore devices, NonceCache nonceCache)
    {
        var path = context.Request.Path.Value ?? "";
        if (UnauthenticatedPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();

        if (!TryGetHeader(context, "X-Client-Id", out var clientId) ||
            !TryGetHeader(context, "X-Timestamp", out var timestampRaw) ||
            !TryGetHeader(context, "X-Nonce", out var nonce) ||
            !TryGetHeader(context, "X-Signature", out var signature) ||
            !long.TryParse(timestampRaw, out var timestamp))
        {
            await WriteError(context, requestId, 400, ErrorCodes.InvalidSignature, "Missing or malformed auth headers.");
            return;
        }

        var device = devices.Find(clientId);
        if (device is null || device.Revoked)
        {
            await WriteError(context, requestId, 401, ErrorCodes.UnknownClient, "Unknown or revoked client.");
            return;
        }

        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp) > TimestampToleranceMs)
        {
            await WriteError(context, requestId, 401, ErrorCodes.StaleRequest, "Request timestamp outside the validity window.");
            return;
        }

        context.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;

        if (!HmacSigner.Verify(device.SharedSecret, context.Request.Method, path, timestamp, nonce, body, signature))
        {
            await WriteError(context, requestId, 401, ErrorCodes.InvalidSignature, "Signature verification failed.");
            return;
        }

        if (!nonceCache.TryRegister(clientId, nonce))
        {
            await WriteError(context, requestId, 401, ErrorCodes.ReplayDetected, "Nonce already used.");
            return;
        }

        devices.UpdateLastSeen(clientId, DateTime.UtcNow);
        context.Items["RequestId"] = requestId;
        context.Items["Device"] = device;

        await _next(context);
    }

    private static bool TryGetHeader(HttpContext context, string name, out string value)
    {
        value = context.Request.Headers[name].FirstOrDefault() ?? "";
        return !string.IsNullOrEmpty(value);
    }

    private static async Task WriteError(HttpContext context, string requestId, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var response = ApiResponse.Fail(requestId, code, message);
        // ApiJson.Options — иначе тут (в отличие от Results.Ok(...) на успешных ответах)
        // JsonSerializer.Serialize без опций сериализует PascalCase-как-в-коде, а не
        // camelCase, который ждёт клиент (Dart ApiClient._parse) — настоящая причина
        // 401/сетевых ошибок (INVALID_SIGNATURE, UNKNOWN_CLIENT и т.п.) тонула в generic
        // "Unknown error". См. Envelope.cs, ApiJson.
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, ApiJson.Options));
    }
}
