using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteShutdown.Agent.Core.Input;

public enum MouseButton { Left, Right }

public enum SpecialKey { Enter, Backspace, Escape, Tab, Space, Delete, ArrowUp, ArrowDown, ArrowLeft, ArrowRight }

/// <summary>
/// Тачпад с телефона: относительное перемещение курсора, клик ЛКМ/ПКМ и ввод текста/
/// спецклавиш — всё через один и тот же низкоуровневый SendInput (WinAPI), как реальные
/// события мыши/клавиатуры, а не через какой-то виртуальный драйвер. См. docs/roadmap.md,
/// "Тачпад".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RemoteInputService
{
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

    /// <summary>Относительное перемещение курсора (пиксели-ish — Windows применяет свою
    /// кривую ускорения, как для обычной мыши; для тачпада это ожидаемо).</summary>
    public void MoveMouse(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE } },
        };
        SendInput(1, new[] { input }, InputSize);
    }

    public void Click(MouseButton button)
    {
        var (down, up) = button == MouseButton.Left
            ? (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
            : (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);

        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = down } } },
            new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = up } } },
        };
        SendInput(2, inputs, InputSize);
    }

    /// <summary>Печатает произвольный текст как есть (Unicode-события клавиатуры) —
    /// работает независимо от текущей раскладки на ПК, в отличие от посимвольного
    /// подбора VK-кодов.</summary>
    public void TypeText(string text)
    {
        foreach (var ch in text)
        {
            var down = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } };
            var up = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            SendInput(2, new[] { down, up }, InputSize);
        }
    }

    public void SendSpecialKey(SpecialKey key)
    {
        var vk = key switch
        {
            SpecialKey.Enter => VK_RETURN,
            SpecialKey.Backspace => VK_BACK,
            SpecialKey.Escape => VK_ESCAPE,
            SpecialKey.Tab => VK_TAB,
            SpecialKey.Space => VK_SPACE,
            SpecialKey.Delete => VK_DELETE,
            SpecialKey.ArrowUp => VK_UP,
            SpecialKey.ArrowDown => VK_DOWN,
            SpecialKey.ArrowLeft => VK_LEFT,
            SpecialKey.ArrowRight => VK_RIGHT,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        var down = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } };
        var up = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } };
        SendInput(2, new[] { down, up }, InputSize);
    }
}
