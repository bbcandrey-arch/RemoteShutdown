namespace RemoteShutdown.Agent.Api;

public static class RequestIdExtensions
{
    public static string GetRequestId(this HttpContext context) =>
        context.Items["RequestId"] as string
        ?? context.Request.Headers["X-Request-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();
}
