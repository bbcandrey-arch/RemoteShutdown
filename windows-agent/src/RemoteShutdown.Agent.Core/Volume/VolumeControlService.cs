using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteShutdown.Agent.Core.Volume;

public enum VolumeAction { Up, Down, Mute, Unmute }

public sealed record VolumeState(int? Level, bool? Muted);

/// <summary>
/// Adjusts system volume by simulating the hardware media keys (VK_VOLUME_UP/DOWN/MUTE).
/// This matches the product decision to expose only +/-/mute (no precise slider) — the
/// simulated-key approach is simpler and more robust than driving IAudioEndpointVolume
/// COM interop, and it respects whatever per-step increment Windows itself uses.
/// Trade-off: because it doesn't read the audio endpoint, GetState() can't report an
/// exact level — only best-effort mute state via waveOutGetVolume as a proxy.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VolumeControlService
{
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private bool? _lastKnownMuted;

    public void Execute(VolumeAction action)
    {
        switch (action)
        {
            case VolumeAction.Up:
                PressKey(VK_VOLUME_UP);
                _lastKnownMuted = false;
                break;
            case VolumeAction.Down:
                PressKey(VK_VOLUME_DOWN);
                break;
            case VolumeAction.Mute:
                PressKey(VK_VOLUME_MUTE);
                _lastKnownMuted = true;
                break;
            case VolumeAction.Unmute:
                // There's no dedicated "unmute" vkey — mute is a toggle, so only send it
                // when we believe we're currently muted; otherwise this would mute instead.
                if (_lastKnownMuted != false)
                {
                    PressKey(VK_VOLUME_MUTE);
                    _lastKnownMuted = false;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>Best-effort state for inclusion in GET /status. Level is null (see class remarks).</summary>
    public VolumeState GetState() => new(Level: null, Muted: _lastKnownMuted);

    private static void PressKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
