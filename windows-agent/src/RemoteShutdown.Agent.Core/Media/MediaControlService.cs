using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteShutdown.Agent.Core.Media;

public enum MediaAction { PlayPause, Next, Previous }

/// <summary>
/// Управление воспроизведением на ПК (плей/пауза, следующий/предыдущий трек) — тем же
/// способом, что и громкость (VolumeControlService): эмуляция аппаратных медиаклавиш
/// клавиатуры (VK_MEDIA_*). Работает с любым плеером, который их слушает (большинство
/// современных — да), без привязки к конкретному приложению. См. docs/roadmap.md.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaControlService
{
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public void Execute(MediaAction action)
    {
        var vk = action switch
        {
            MediaAction.PlayPause => VK_MEDIA_PLAY_PAUSE,
            MediaAction.Next => VK_MEDIA_NEXT_TRACK,
            MediaAction.Previous => VK_MEDIA_PREV_TRACK,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
