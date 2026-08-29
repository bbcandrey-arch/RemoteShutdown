using RemoteShutdown.Agent.Api;
using RemoteShutdown.Agent.Api.Endpoints;
using RemoteShutdown.Agent.Api.Events;
using RemoteShutdown.Agent.Api.Middleware;
using RemoteShutdown.Agent.Core.Events;
using RemoteShutdown.Agent.Core.Metrics;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;
using RemoteShutdown.Agent.Core.Timers;
using RemoteShutdown.Agent.Core.Volume;

var builder = WebApplication.CreateBuilder(args);

const int DefaultPort = 54321;

// Storage + settings need to exist before we can decide the port / load the cert.
var db = new AgentDatabase();
db.EnsureCreated();
var settingsStore = new SettingsStore(db);
var port = int.TryParse(settingsStore.Get(SettingsStore.Keys.Port), out var configuredPort) ? configuredPort : DefaultPort;
if (settingsStore.Get(SettingsStore.Keys.Port) is null)
    settingsStore.Set(SettingsStore.Keys.Port, DefaultPort.ToString());

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listenOptions =>
    {
        var cert = CertificateProvider.GetOrCreate(settingsStore);
        listenOptions.UseHttps(cert);
    });
});

builder.Services.AddMemoryCache();
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(settingsStore);
builder.Services.AddSingleton<PairedDeviceStore>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton<NonceCache>();
builder.Services.AddSingleton<PowerActionsService>();
builder.Services.AddSingleton<VolumeControlService>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<TimerRepository>();
builder.Services.AddSingleton<TimerSchedulerService>();
builder.Services.AddSingleton<WebSocketEventHub>();
builder.Services.AddSingleton<IAgentEventPublisher>(sp => sp.GetRequiredService<WebSocketEventHub>());
builder.Services.AddHostedService<MetricsBroadcastService>();

var app = builder.Build();

app.UseWebSockets();
app.UseMiddleware<HmacAuthMiddleware>();

app.MapPairEndpoints();
app.MapCommandEndpoints();
app.MapTimerEndpoints();
app.MapStatusEndpoints();

app.Map("/events", async (HttpContext context, WebSocketEventHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.HandleConnectionAsync(socket, context.RequestAborted);
});

// Restore any timers that were pending before this run (agent restart/crash), per
// docs/protocol.md §7 and the "Хранение состояния таймеров" section of the plan.
app.Services.GetRequiredService<TimerSchedulerService>().RestoreFromStorage();

app.Run();
