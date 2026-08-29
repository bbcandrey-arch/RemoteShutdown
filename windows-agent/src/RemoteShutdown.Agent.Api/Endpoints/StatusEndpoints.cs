using System.Reflection;
using RemoteShutdown.Agent.Core.Metrics;
using RemoteShutdown.Agent.Core.Network;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Volume;

namespace RemoteShutdown.Agent.Api.Endpoints;

public static class StatusEndpoints
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/status", (HttpContext ctx, PairedDeviceStore devices, VolumeControlService volume) =>
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
            var state = volume.GetState();
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), new
            {
                online = true,
                hostname = Environment.MachineName,
                deviceMac = NetworkInfoService.GetPrimaryMacAddress(),
                agentVersion = version,
                uptimeSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
                volume = new { level = state.Level, muted = state.Muted },
                pairedDevicesCount = devices.ListAll().Count(d => !d.Revoked),
            }));
        });

        app.MapGet("/metrics", (HttpContext ctx, MetricsCollector metrics) =>
        {
            var snapshot = metrics.Collect();
            return Results.Ok(ApiResponse.Ok(ctx.GetRequestId(), ToPayload(snapshot)));
        });
    }

    public static object ToPayload(MetricsSnapshot snapshot) => new
    {
        cpuPercent = snapshot.CpuPercent,
        ramUsedBytes = snapshot.RamUsedBytes,
        ramTotalBytes = snapshot.RamTotalBytes,
        disks = snapshot.Disks.Select(d => new { drive = d.Drive, usedBytes = d.UsedBytes, totalBytes = d.TotalBytes }),
    };
}
