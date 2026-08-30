using System.ComponentModel;
using System.Diagnostics;

namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Правило Windows-брандмауэра для входящих подключений к агенту (docs/roadmap.md) —
/// без него первый запуск на новой машине обычно спотыкается о молчаливо заблокированный
/// порт (брандмауэр Windows не спрашивает разрешения для процесса, слушающего
/// произвольный TCP-порт не через явную регистрацию приложения). Кнопка в настройках
/// избавляет от похода в "Разрешить приложению взаимодействие с брандмауэром" вручную.
/// </summary>
public static class FirewallRuleService
{
    private const string RuleName = "RemoteShutdownAgent";

    /// <summary>
    /// Проверка не требует прав администратора (netsh ...show... доступен обычному
    /// пользователю, в отличие от add/delete) — можно смело вызывать при каждом
    /// открытии вкладки "Общие".
    /// </summary>
    public static bool RuleExists()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe", $"advfirewall firewall show rule name=\"{RuleName}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Если netsh недоступен/что-то пошло не так — считаем, что правила нет,
            // кнопка "Добавить" в SettingsForm всё равно останется рабочим путём.
            return false;
        }
    }

    /// <summary>
    /// Добавляет/обновляет правило входящего TCP на указанный порт. Требует прав
    /// администратора — запрашивается через стандартный UAC-диалог (сам процесс трея
    /// повышать не нужно). Старое правило с этим именем сначала удаляется, чтобы клик
    /// был идемпотентным и подхватывал новый порт, если он менялся в настройках, вместо
    /// накопления дублей.
    /// </summary>
    public static bool AddOrUpdateRule(int port, out string message)
    {
        var command =
            $"netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}";

        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                message = "Не удалось запустить процесс настройки брандмауэра.";
                return false;
            }

            process.WaitForExit(15000);
            if (!process.HasExited)
            {
                message = "Команда не завершилась вовремя.";
                return false;
            }

            if (process.ExitCode != 0)
            {
                message = $"netsh вернул код ошибки {process.ExitCode}.";
                return false;
            }

            message = $"Правило добавлено: входящие TCP-подключения на порт {port} разрешены.";
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — пользователь отклонил UAC
        {
            message = "Отменено: нужны права администратора, чтобы добавить правило брандмауэра.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Ошибка: {ex.Message}";
            return false;
        }
    }
}
