using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using RemoteShutdown.Agent.Core.Security;

namespace RemoteShutdown.Agent.Core.Storage;

/// <summary>
/// Значения по умолчанию для новой/незнакомой БД (первый запуск агента) — вынесены из
/// Program.cs в отдельный класс, чтобы это было проверяемо юнит-тестом (на временной
/// БД, не трогая реальный agent.db) и не разбросано отдельными "if Get(...) is null"
/// по точке входа.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentDefaults
{
    public const int DefaultPort = 54321;

    /// <param name="GeneratedPin">
    /// PIN, только что сгенерированный для этого первого запуска (см. Apply) — null,
    /// если PIN уже был задан раньше (обычный случай на не самом первом запуске).
    /// Только для первичного уведомления пользователя (Program.cs выводит его в
    /// консоль) — сам PIN уже сохранён хэшем в Settings, дальше он читается оттуда же,
    /// что и любой другой PIN, установленный через SettingsForm.
    /// </param>
    public sealed record ApplyResult(string? GeneratedPin);

    public static ApplyResult Apply(SettingsStore settings)
    {
        if (settings.Get(SettingsStore.Keys.Port) is null)
            settings.Set(SettingsStore.Keys.Port, DefaultPort.ToString());

        // Тестовый режим по умолчанию ВЫКЛЮЧЕН — это боевой инструмент выключения ПК, а
        // не отладочная песочница по умолчанию; включать его должен осознанно сам
        // пользователь в настройках трея, когда нужно проверить работу без реального
        // выключения/перезагрузки/сна. См. docs/security.md.
        //
        // ВАЖНО: это значение применяется, ТОЛЬКО если ключа test_mode в Settings ещё
        // нет вовсе — на уже существующей БД (ключ уже сохранён, каким бы ни было
        // значение — true или false) эта строка ничего не меняет и не трогает.
        if (settings.Get(SettingsStore.Keys.TestMode) is null)
            settings.Set(SettingsStore.Keys.TestMode, "false");

        // Имя ПК по умолчанию — реальное сетевое имя машины; пользователь может
        // переименовать в SettingsForm ("Общие"), см. docs/roadmap.md.
        if (settings.Get(SettingsStore.Keys.DeviceName) is null)
            settings.Set(SettingsStore.Keys.DeviceName, Environment.MachineName);

        // PIN для сопряжения — раньше на первом запуске оставался не задан (пейринг был
        // невозможен, пока пользователь сам не откроет настройки и не введёт PIN
        // вручную). Теперь генерируем случайный 6-значный сразу, чтобы сопряжение
        // работало "из коробки" — PIN виден в SettingsForm (кнопка "Показать") тем же
        // способом, что и заданный вручную.
        string? generatedPin = null;
        if (settings.Get(SettingsStore.Keys.PinHash) is null)
        {
            generatedPin = GenerateRandomPin();
            settings.Set(SettingsStore.Keys.PinHash, PinHasher.Hash(generatedPin));
            settings.Set(SettingsStore.Keys.PinPlainProtected,
                Convert.ToBase64String(DpapiProtector.ProtectPin(Encoding.UTF8.GetBytes(generatedPin))));
        }

        return new ApplyResult(generatedPin);
    }

    private static string GenerateRandomPin()
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        var value = BitConverter.ToUInt32(buffer) % 1_000_000u;
        return value.ToString("D6");
    }
}
