using RemoteShutdown.Agent.Core.Input;
using RemoteShutdown.Agent.Core.Ipc;
using RemoteShutdown.Agent.Core.Media;
using RemoteShutdown.Agent.Core.Power;
using RemoteShutdown.Agent.Core.Storage;
using RemoteShutdown.Agent.Core.Volume;

namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Владеет иконкой в трее и мостом в интерактивную сессию (SessionRelayClient) — сам
/// HTTP-агент теперь постоянно живёт в RemoteShutdown.Agent.Service (Windows Service,
/// см. docs/roadmap.md, "Windows Service"), Tray больше НЕ запускает и не останавливает
/// его как дочерний процесс. Роль Tray — выполнять то, что физически требует рабочего
/// стола (громкость, медиа, блокировка, тачпад), когда Service (Session 0) просит об
/// этом через именованный канал.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AgentDatabase _db;
    private readonly SettingsStore _settingsStore;
    private readonly TaskLogStore _taskLog;
    private readonly System.Windows.Forms.Timer _taskLogPoller;
    private readonly PowerActionsService _power;
    private readonly VolumeControlService _volume = new();
    private readonly MediaControlService _media = new();
    private readonly RemoteInputService _input = new();
    private readonly SessionRelayClient _relayClient;
    private readonly System.Windows.Forms.Timer _singleInstanceWatcher;
    private int _lastSeenTaskLogId;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _db = new AgentDatabase();
        _db.EnsureCreated();
        _settingsStore = new SettingsStore(_db);
        _taskLog = new TaskLogStore(_db);
        _power = new PowerActionsService(_settingsStore);
        // Единая точка дефолтов первого запуска (test_mode по умолчанию ВЫКЛЮЧЕН —
        // см. AgentDefaults.cs) — Service тоже его вызывает, идемпотентно; держим и
        // здесь на случай, если Tray стартует раньше Service (гонка при входе в систему).
        AgentDefaults.Apply(_settingsStore);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Настройки", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Перезапустить службу агента", null, (_, _) => RestartService());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "Remote Shutdown Agent",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        // Не уведомляем о задачах, случившихся до запуска трея (например, восстановленных
        // таймеров прошлой сессии) — только о новых, прилетевших пока трей открыт.
        _lastSeenTaskLogId = _taskLog.GetMaxId();

        // Поллинг вместо WebSocket/IPC: и трей, и Service читают одну и ту же SQLite-БД,
        // отдельный канал связи для журнала не нужен — см. docs/roadmap.md,
        // "Уведомления и журнал задач".
        _taskLogPoller = new System.Windows.Forms.Timer { Interval = 3000 };
        _taskLogPoller.Tick += (_, _) => PollTaskLog();
        _taskLogPoller.Start();

        // Подключение к Service по именованному каналу — сам переподключается, если
        // Service ещё не поднялась или канал порвался (см. SessionRelayClient).
        _relayClient = new SessionRelayClient(HandleRelayRequestAsync);

        // Повторный запуск Tray (ярлык нажали ещё раз) не создаёт вторую копию — та
        // просто просит эту, уже работающую, показать настройки и сама сразу выходит
        // (см. SingleInstanceGuard, Program.cs). Слушаем этот сигнал здесь.
        _singleInstanceWatcher = SingleInstanceGuard.WatchForSignal(ShowSettings);
    }

    /// <summary>Выполняет то, что Service (Session 0) сама сделать не может —
    /// см. RelayKinds. Вызывается из фонового потока SessionRelayClient.</summary>
    private Task<RelayResponse> HandleRelayRequestAsync(RelayRequest request)
    {
        try
        {
            switch (request.Kind)
            {
                case RelayKinds.Lock:
                    _power.Execute(PowerAction.Lock);
                    break;

                case RelayKinds.Volume:
                    if (!Enum.TryParse<VolumeAction>(request.Params.GetValueOrDefault("action"), ignoreCase: true, out var volumeAction))
                        return Task.FromResult(RelayResponse.Failure(request.Id, "Unknown volume action."));
                    _volume.Execute(volumeAction);
                    break;

                case RelayKinds.Media:
                    if (!Enum.TryParse<MediaAction>(request.Params.GetValueOrDefault("action"), ignoreCase: true, out var mediaAction))
                        return Task.FromResult(RelayResponse.Failure(request.Id, "Unknown media action."));
                    _media.Execute(mediaAction);
                    break;

                case RelayKinds.MouseMove:
                    var dx = int.TryParse(request.Params.GetValueOrDefault("dx"), out var dxv) ? dxv : 0;
                    var dy = int.TryParse(request.Params.GetValueOrDefault("dy"), out var dyv) ? dyv : 0;
                    _input.MoveMouse(dx, dy);
                    break;

                case RelayKinds.MouseClick:
                    if (!Enum.TryParse<MouseButton>(request.Params.GetValueOrDefault("button"), ignoreCase: true, out var button))
                        return Task.FromResult(RelayResponse.Failure(request.Id, "Unknown mouse button."));
                    _input.Click(button);
                    break;

                case RelayKinds.KeyboardText:
                    _input.TypeText(request.Params.GetValueOrDefault("text") ?? "");
                    break;

                case RelayKinds.KeyboardKey:
                    if (!Enum.TryParse<SpecialKey>(request.Params.GetValueOrDefault("key"), ignoreCase: true, out var key))
                        return Task.FromResult(RelayResponse.Failure(request.Id, "Unknown key."));
                    _input.SendSpecialKey(key);
                    break;

                default:
                    return Task.FromResult(RelayResponse.Failure(request.Id, $"Unknown relay kind '{request.Kind}'."));
            }
            return Task.FromResult(RelayResponse.Success(request.Id));
        }
        catch (Exception ex)
        {
            return Task.FromResult(RelayResponse.Failure(request.Id, ex.Message));
        }
    }

    private void PollTaskLog()
    {
        var newEntries = _taskLog.ListSince(_lastSeenTaskLogId);
        if (newEntries.Count == 0) return;

        _lastSeenTaskLogId = newEntries[^1].Id;
        foreach (var entry in newEntries)
        {
            var icon = entry.Kind == "error" ? ToolTipIcon.Error : ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(5000, "Remote Shutdown Agent", entry.Description, icon);
        }

        // Если открыто окно настроек на вкладке "Журнал" — сразу обновляем список.
        _settingsForm?.RefreshTaskLogIfVisible();
    }

    private void RestartService()
    {
        var (ok, message) = ServiceControlService.Restart();
        if (!ok)
            _notifyIcon.ShowBalloonTip(5000, "Remote Shutdown Agent", message, ToolTipIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            // Окно после закрытия крестиком не уничтожается, а просто прячется
            // (SettingsForm, hideInsteadOfClose) — Activate() сам по себе не показывает
            // скрытое окно, он только переводит фокус на уже видимое. Без Show() здесь
            // повторные двойной клик/"Настройки" из меню трея молча ничего не делали
            // после первого закрытия окна — тот самый баг.
            _settingsForm.Show();
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_db, _settingsStore, taskLog: _taskLog);
        _settingsForm.Show();
    }

    private void ExitApplication()
    {
        _taskLogPoller.Stop();
        _singleInstanceWatcher.Stop();
        _notifyIcon.Visible = false;
        _relayClient.Dispose();
        // Если окно настроек сейчас открыто, его FormClosing (см. SettingsForm,
        // hideInsteadOfClose) иначе отменил бы попытку Application.Exit() закрыть его —
        // и тем самым тихо гасил весь выход целиком: иконка уже пропадала (строка выше),
        // а сам процесс оставался висеть. Баг-репорт: "Выход" не убивал процесс именно
        // когда окно настроек было открыто.
        _settingsForm?.AllowRealClose();
        Application.Exit();
    }
}
