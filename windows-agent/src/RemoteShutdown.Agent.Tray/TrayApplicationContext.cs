using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Tray;

/// <summary>Владеет иконкой в трее и жизненным циклом дочернего процесса агента.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AgentProcessManager _agentProcess = new();
    private readonly AgentDatabase _db;
    private readonly SettingsStore _settingsStore;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _db = new AgentDatabase();
        _db.EnsureCreated();
        _settingsStore = new SettingsStore(_db);
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

        TryStartAgent();
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
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_db, _settingsStore);
        _settingsForm.Show();
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _agentProcess.Stop();
        Application.Exit();
    }
}
