using System.Drawing;
using System.Runtime.InteropServices;

namespace RemoteHost;

/// <summary>Maps normalized coords to primary screen pixels; move via SetCursorPos, buttons via SendInput.</summary>
public class InputInjector
{
    private readonly Rectangle _bounds;

    public InputInjector()
    {
        var screen = Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary screen.");
        _bounds = screen.Bounds;
    }

    protected InputInjector(Rectangle bounds)
    {
        _bounds = bounds;
    }

    public virtual void MoveNormalized(double nx, double ny)
    {
        var (x, y) = ToPixels(nx, ny);
        SetCursorPos(x, y);
    }

    public virtual void ButtonDownNormalized(double nx, double ny, int button)
    {
        var (x, y) = ToPixels(nx, ny);
        SetCursorPos(x, y);
        var flags = button switch
        {
            1 => MOUSEEVENTF_RIGHTDOWN,
            2 => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN,
        };
        SendMouseClick(flags);
    }

    public virtual void ButtonUpNormalized(double nx, double ny, int button)
    {
        var (x, y) = ToPixels(nx, ny);
        SetCursorPos(x, y);
        var flags = button switch
        {
            1 => MOUSEEVENTF_RIGHTUP,
            2 => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP,
        };
        SendMouseClick(flags);
    }

    public virtual void Wheel(int dx, int dy)
    {
        if (dy != 0)
            SendMouseClick(MOUSEEVENTF_WHEEL, (uint)(dy * WHEEL_DELTA));
        if (dx != 0)
            SendMouseClick(MOUSEEVENTF_HWHEEL, (uint)(dx * WHEEL_DELTA));
    }

    public virtual void Key(ushort virtualKey, bool down)
    {
        var flags = down ? 0u : KEYEVENTF_KEYUP;
        SendKeyboardInput(virtualKey, flags);
    }

    public virtual void Text(string s)
    {
        foreach (var ch in s)
        {
            SendUnicode(ch, true);
            SendUnicode(ch, false);
        }
    }

    private (int x, int y) ToPixels(double nx, double ny)
    {
        var w = Math.Max(1, _bounds.Width);
        var h = Math.Max(1, _bounds.Height);
        var x = (int)Math.Clamp(nx * w, 0, w - 1) + _bounds.Left;
        var y = (int)Math.Clamp(ny * h, 0, h - 1) + _bounds.Top;
        return (x, y);
    }

    private static void SendMouseClick(uint flags, uint data = 0)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = data,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = 0,
                    },
                },
            },
        };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyboardInput(ushort vk, uint flags)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = 0,
                    },
                },
            },
        };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicode(char ch, bool keyDown)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE | (keyDown ? 0u : KEYEVENTF_KEYUP),
                        time = 0,
                        dwExtraInfo = 0,
                    },
                },
            },
        };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const int WHEEL_DELTA = 120;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);
}