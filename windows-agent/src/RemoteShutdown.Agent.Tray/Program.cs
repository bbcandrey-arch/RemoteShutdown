namespace RemoteShutdown.Agent.Tray;

internal static class Program
{
    /// <summary>
    /// Трей-приложение — единственная видимая часть агента для пользователя (docs/roadmap.md,
    /// "UX опасных команд и управление доступом"): иконка в трее + окно настроек (пейринг/QR,
    /// сопряжённые устройства, тестовый режим, автозагрузка), плюс мост в интерактивную
    /// сессию (SessionRelayClient) для команд, которые не может выполнить сама
    /// RemoteShutdown.Agent.Service — см. TrayApplicationContext.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // "--settings": сразу открыть окно настроек (без ожидания клика по иконке трея) —
        // удобно и для быстрой проверки, и как альтернативный способ запуска для пользователя.
        if (args.Contains("--settings"))
        {
            var db = new Core.Storage.AgentDatabase();
            db.EnsureCreated();
            var settingsStore = new Core.Storage.SettingsStore(db);
            Core.Storage.AgentDefaults.Apply(settingsStore);
            Application.Run(new SettingsForm(db, settingsStore, hideInsteadOfClose: false));
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
