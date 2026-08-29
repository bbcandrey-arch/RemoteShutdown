using Microsoft.Win32;

namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Автозагрузка трей-приложения при входе в Windows (docs/roadmap.md, "UX опасных
/// команд и управление доступом") — через стандартный ключ автозапуска HKCU, без
/// прав администратора и без отдельного установщика.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RemoteShutdownAgentTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existing && existing == ExecutablePath();
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
            key.SetValue(ValueName, ExecutablePath());
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string ExecutablePath() => $"\"{Environment.ProcessPath}\"";
}
