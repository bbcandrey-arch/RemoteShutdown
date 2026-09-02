using System.ComponentModel;
using System.Diagnostics;

namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Перезапуск RemoteShutdown.Agent.Service (Windows Service, см. AgentHost.cs) из меню
/// трея — тот же приём, что и FirewallRuleService: UAC-запрос через `runas`, сам процесс
/// трея прав администратора не требует.
/// </summary>
public static class ServiceControlService
{
    public const string ServiceName = "RemoteShutdownAgent";

    /// <summary>Возвращает (успех, сообщение для показа пользователю при неудаче).</summary>
    public static (bool Ok, string Message) Restart()
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c net stop {ServiceName} & net start {ServiceName}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Не удалось запустить процесс перезапуска службы.");

            process.WaitForExit(20000);
            if (!process.HasExited)
                return (false, "Перезапуск службы не завершился вовремя.");

            // net start возвращает не-0, если служба уже была запущена другим стечением
            // обстоятельств гонки (stop/start подряд) — не считаем это фатальной ошибкой,
            // просто сообщаем как есть.
            return process.ExitCode == 0
                ? (true, "Служба агента перезапущена.")
                : (false, $"net вернул код {process.ExitCode} — проверьте службу '{ServiceName}' в services.msc.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — пользователь отклонил UAC
        {
            return (false, "Отменено: нужны права администратора, чтобы перезапустить службу.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка: {ex.Message}");
        }
    }
}
