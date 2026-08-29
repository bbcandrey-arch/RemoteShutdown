using System.Text.Json;
using QRCoder;
using RemoteShutdown.Agent.Core.Network;

namespace RemoteShutdown.Agent.Api;

/// <summary>
/// Генерирует QR-код для экрана сопряжения (docs/roadmap.md, "QR-код пейринг", MVP-версия):
/// кодирует только host+port, чтобы телефону не нужно было вводить их вручную. PIN
/// пользователь по-прежнему вводит сам — так сохраняется двухфакторность пейринга
/// (см. docs/security.md): "знание IP из QR" + "знание PIN" не совпадают в одном канале,
/// если камера случайно поймает чужой QR-код, PIN всё равно защитит от чужого пейринга.
/// </summary>
public static class PairingQrService
{
    public sealed record PairingQrPayload(string host, int port);

    /// <summary>
    /// Сохраняет PNG с QR-кодом рядом с БД агента и возвращает путь к файлу, либо null,
    /// если не удалось определить локальный IPv4-адрес (например, нет сетевых интерфейсов).
    /// </summary>
    public static string? GenerateAndSave(int port, string outputDirectory)
    {
        var host = NetworkInfoService.GetPrimaryIPv4Address();
        if (host is null) return null;

        var payload = JsonSerializer.Serialize(new PairingQrPayload(host, port));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 10);

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "pairing-qr.png");
        File.WriteAllBytes(path, png);
        return path;
    }
}
