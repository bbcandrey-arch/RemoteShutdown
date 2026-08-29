using Microsoft.Extensions.Hosting;
using RemoteShutdown.Agent.Api.Endpoints;
using RemoteShutdown.Agent.Core.Events;
using RemoteShutdown.Agent.Core.Metrics;

namespace RemoteShutdown.Agent.Api.Events;

/// <summary>Pushes a metricsUpdate WS event every few seconds (docs/protocol.md §8).</summary>
public sealed class MetricsBroadcastService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly MetricsCollector _metrics;
    private readonly IAgentEventPublisher _events;

    public MetricsBroadcastService(MetricsCollector metrics, IAgentEventPublisher events)
    {
        _metrics = metrics;
        _events = events;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var snapshot = _metrics.Collect();
                await _events.PublishAsync("metricsUpdate", StatusEndpoints.ToPayload(snapshot), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
