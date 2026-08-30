using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Tray;

/// <summary>Владеет иконкой в трее и жизненным циклом дочернего процесса агента.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AgentProcessManager _agentProcess = new();
    private readonly AgentDatabase _db;
    private readonly SettingsStore _settingsStore;
    private readonly TaskLogStore _taskLog;
    private readonly System.Windows.Forms.Timer _taskLogPoller;
    private int _lastSeenTaskLogId;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _db = new AgentDatabase();
        _db.EnsureCreated();
        _settingsStore = new SettingsStore(_db);
        _taskLog = new TaskLogStore(_db);
        if (_settingsStore.Get(SettingsStore.Keys.TestMode) is null)
            _settingsStore.Set(SettingsStore.Keys.TestMode, "true");

        var menu = new ContextMenuStrip();
        menu.Items.Add("Настройки", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Перезапустить агент", null, (_, _) => RestartAgent());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Remote Shutdown Agent",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        // Не уведомляем о задачах, случившихся до запуска трея (например, восстановленных
        // таймеров прошлой сессии) — только о новых, прилетевших пока трей открыт.
        _lastSeenTaskLogId = _taskLog.GetMaxId();

        // Поллинг вместо WebSocket/IPC: и трей, и Api читают одну и ту же SQLite-БД,
        // отдельный канал связи между процессами не нужен — см. docs/roadmap.md,
        // "Уведомления и журнал задач".
        _taskLogPoller = new System.Windows.Forms.Timer { Interval = 3000 };
        _taskLogPoller.Tick += (_, _) => PollTaskLog();
        _taskLogPoller.Start();

        TryStartAgent();
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

    private void TryStartAgent()
    {
        var exePath = AgentProcessManager.FindAgentApiExecutable();
        if (exePath is null)
        {
            _notifyIcon.ShowBalloonTip(5000, "Remote Shutdown Agent",
                "Не найден RemoteShutdown.Agent.Api.exe рядом с решением — соберите проект (dotnet build) и перезапустите трей.",
                ToolTipIcon.Warning);
            return;
        }
        _agentProcess.Start(exePath);
    }

    private void RestartAgent()
    {
        _agentProcess.Stop();
        TryStartAgent();
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
        _notifyIcon.Visible = false;
        _agentProcess.Stop();
        Application.Exit();
    }
}
