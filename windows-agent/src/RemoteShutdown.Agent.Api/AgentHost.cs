using RemoteShutdown.Agent.Api.Endpoints;
using RemoteShutdown.Agent.Api.Events;
using RemoteShutdown.Agent.Api.Middleware;
using RemoteShutdown.Agent.Core.Events;
using RemoteShutdown.Agent.Core.Input;
using RemoteShutdown.Agent.Core.Ipc;
using RemoteShutdown.Agent.Core.Media;
using RemoteShutdown.Agent.Core.Metrics;
using RemoteShutdown.Agent.Core.Pairing;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;
using RemoteShutdown.Agent.Core.Timers;
using RemoteShutdown.Agent.Core.Volume;

namespace RemoteShutdown.Agent.Api;

/// <summary>
/// Строит агент (DI, Kestrel, эндпоинты) — вынесено из Program.cs, чтобы один и тот же
/// код запускало и RemoteShutdown.Agent.Api.exe (интерактивный запуск/разработка,
/// `dotnet run`), и RemoteShutdown.Agent.Service.exe (Windows Service, поднимается при
/// загрузке ОС ещё до входа пользователя — см. docs/roadmap.md, "Windows Service").
/// builder.Host.UseWindowsService(...) ничего не делает, если процесс не был запущен
/// диспетчером служб — тот же билд одинаково хорошо работает и из консоли.
/// </summary>
public static class AgentHost
{
    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(options => options.ServiceName = "RemoteShutdownAgent");

        // Storage + settings need to exist before we can decide the port / load the cert.
        var db = new AgentDatabase();
        db.EnsureCreated();

        // Служба работает от LocalSystem, а Tray — от обычного пользователя, но пишет в
        // ту же папку (agent.db, pairing-qr.png) — без явного гранта здесь у обычных
        // пользователей по умолчанию нет права записи в {commonappdata}, и Tray падает
        // с UnauthorizedAccessException при первом же открытии настроек (см.
        // AgentDataFolderAcl). Делаем это здесь, а не в конструкторе AgentDatabase, —
        // Tray тоже создаёт AgentDatabase, но у него самого не хватит прав раздать этот
        // грант, и делать это в общем коде для обоих процессов только зря шумело бы.
        AgentDataFolderAcl.EnsureWritableByInteractiveUsers(Path.GetDirectoryName(db.DbPath)!);

        var settingsStore = new SettingsStore(db);

        // Порт/тестовый режим/имя ПК/PIN по умолчанию для незнакомой БД — см. AgentDefaults
        // (вынесено туда, а не оставлено тут, чтобы это было покрыто юнит-тестом на временной
        // БД, не трогая реальный agent.db).
        var defaultsResult = AgentDefaults.Apply(settingsStore);
        if (defaultsResult.GeneratedPin is { } generatedPin)
            Console.WriteLine($"Первый запуск: сгенерирован PIN для сопряжения — {generatedPin} (посмотреть снова можно в настройках трея, кнопка «Показать»).");

        var port = int.TryParse(settingsStore.Get(SettingsStore.Keys.Port), out var configuredPort) ? configuredPort : AgentDefaults.DefaultPort;

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
        builder.Services.AddSingleton<RemoteInputService>();
        builder.Services.AddSingleton<MetricsCollector>();
        builder.Services.AddSingleton<TimerRepository>();
        builder.Services.AddSingleton<TaskLogStore>();
        builder.Services.AddSingleton<TimerSchedulerService>();
        builder.Services.AddSingleton<WebSocketEventHub>();
        builder.Services.AddSingleton<IAgentEventPublisher>(sp => sp.GetRequiredService<WebSocketEventHub>());
        // Мост в интерактивную сессию (громкость/медиа/блокировка/тачпад) — см.
        // SessionRelayServer. Живёт независимо от того, залогинен ли кто-то сейчас;
        // если Tray не подключён, вызовы просто возвращают NO_ACTIVE_SESSION.
        builder.Services.AddSingleton<SessionRelayServer>();
        builder.Services.AddHostedService<MetricsBroadcastService>();

        var app = builder.Build();

        app.UseWebSockets();
        app.UseMiddleware<HmacAuthMiddleware>();

        app.MapPairEndpoints();
        app.MapCommandEndpoints();
        app.MapInputEndpoints();
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

        // Инициализируем SessionRelayServer сразу (не по требованию первого запроса) —
        // именованный канал должен слушать подключение Tray с самого старта агента.
        _ = app.Services.GetRequiredService<SessionRelayServer>();

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

        return app;
    }
}
