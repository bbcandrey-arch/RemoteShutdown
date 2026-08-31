namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Иконка трея — та же, что зашита в exe через ApplicationIcon (windows-agent/assets/app.ico,
/// тот же power-symbol, что и у Android-приложения). Достаём её из самого запущенного exe,
/// а не грузим отдельный файл — так не нужно тащить .ico рядом с published-бинарником.
/// </summary>
internal static class AppIcon
{
    public static Icon Load()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) return icon;
        }
        catch
        {
            // Падать из-за иконки в трее не должны — на крайний случай системная.
        }
        return SystemIcons.Application;
    }
}
