using RemoteShutdown.Agent.Api;
using RemoteShutdown.Agent.Api.Endpoints;
using RemoteShutdown.Agent.Api.Events;
using RemoteShutdown.Agent.Api.Middleware;
using RemoteShutdown.Agent.Core.Events;
using RemoteShutdown.Agent.Core.Media;
using RemoteShutdown.Agent.Core.Metrics;
using RemoteShutdown.Agent.Core.Pairing;
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

// Порт/тестовый режим/имя ПК/PIN по умолчанию для незнакомой БД — см. AgentDefaults
// (вынесено туда, а не оставлено тут, чтобы это было покрыто юнит-тестом на временной
// БД, не трогая реальный agent.db).
var defaultsResult = AgentDefaults.Apply(settingsStore);
if (defaultsResult.GeneratedPin is { } generatedPin)
    Console.WriteLine($"Первый запуск: сгенерирован PIN для сопряжения — {generatedPin} (посмотреть снова можно в настройках трея, кнопка «Показать»).");

var port = int.TryParse(settingsStore.Get(SettingsStore.Keys.Port), out var configuredPort) ? configuredPort : DefaultPort;

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
builder.Services.AddSingleton(sp => new PowerActionsService(sp.GetRequiredService<SettingsStore>()));
builder.Services.AddSingleton<VolumeControlService>();
builder.Services.AddSingleton<MediaControlService>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<TimerRepository>();
builder.Services.AddSingleton<TaskLogStore>();
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

// QR-код для экрана сопряжения (docs/roadmap.md, "QR-код пейринг") — сохраняем рядом
// с БД агента, чтобы пользователь мог открыть картинку и показать её камере телефона.
var qrDirectory = Path.GetDirectoryName(db.DbPath) ?? AppContext.BaseDirectory;
var preferredIp = settingsStore.Get(SettingsStore.Keys.PreferredIp);
// PIN зашивается в QR, только если известен в открытом виде (задавался через SettingsForm
// в трее) — см. RemoteShutdown.Agent.Tray/SettingsForm.cs и docs/security.md.
string? knownPin = null;
if (settingsStore.Get(SettingsStore.Keys.PinPlainProtected) is { } protectedPinBase64)
{
    try { knownPin = System.Text.Encoding.UTF8.GetString(DpapiProtector.UnprotectPin(Convert.FromBase64String(protectedPinBase64))); }
    catch (System.Security.Cryptography.CryptographicException) { /* см. DpapiProtector — не критично для запуска */ }
}
var deviceName = settingsStore.Get(SettingsStore.Keys.DeviceName) ?? Environment.MachineName;
var qrPath = PairingQrService.GenerateAndSave(port, qrDirectory, preferredIp, knownPin, deviceName);
if (qrPath is not null)
    Console.WriteLine($"QR-код для сопряжения сохранён: {qrPath}");
else
    Console.WriteLine("Не удалось определить локальный IP-адрес для QR-кода сопряжения — используйте ручной ввод IP.");

app.Run();
