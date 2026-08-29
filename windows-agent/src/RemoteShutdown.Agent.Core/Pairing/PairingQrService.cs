using System.Text.Json;
using QRCoder;
using RemoteShutdown.Agent.Core.Network;

namespace RemoteShutdown.Agent.Core.Pairing;

/// <summary>
/// Генерирует QR-код для экрана сопряжения (docs/roadmap.md, "QR-код пейринг"): кодирует
/// host+port и, если он известен в открытом виде (задан через SettingsForm, см.
/// DpapiProtector.ProtectPin), сам PIN — так сканирование одним кадром заменяет весь
/// ручной ввод. Раздельный ввод IP/порта и PIN не даёт защиты от постороннего: тот, кто
/// видит экран ПК достаточно, чтобы сфотографировать QR, точно так же прочитал бы PIN
/// текстом — значит, объединение в один код не ослабляет защиту, только удобнее.
/// </summary>
public static class PairingQrService
{
    public sealed record PairingQrPayload(string host, int port, string? pin);

    /// <summary>
    /// Сохраняет PNG с QR-кодом рядом с БД агента и возвращает путь к файлу, либо null,
    /// если не удалось определить локальный IPv4-адрес (например, нет сетевых интерфейсов).
    /// </summary>
    /// <param name="host">
    /// Явный IP (например, выбранный пользователем интерфейс в Settings UI, когда на
    /// машине их несколько) — если null, берётся первый найденный автоматически.
    /// </param>
    /// <param name="pin">
    /// PIN в открытом виде, если он известен (см. SettingsStore.Keys.PinPlainProtected) —
    /// null, если PIN никогда не устанавливали через SettingsForm (например, задан только
    /// хэш вручную), тогда QR несёт только host+port, как раньше.
    /// </param>
    public static string? GenerateAndSave(int port, string outputDirectory, string? host = null, string? pin = null)
    {
        host ??= NetworkInfoService.GetPrimaryIPv4Address();
        if (host is null) return null;

        var payload = JsonSerializer.Serialize(new PairingQrPayload(host, port, pin));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 10);

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "pairing-qr.png");
        File.WriteAllBytes(path, png);
        return path;
    }
}
