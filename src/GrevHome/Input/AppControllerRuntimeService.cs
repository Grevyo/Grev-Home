using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrevHome.Input;

/// <summary>
/// Executes the standardized outputs stored in an app controller profile. Keyboard shortcuts
/// are sent only after the caller has restored the intended managed app to the foreground.
/// Mouse cursor/click outputs intentionally act on the current desktop target so they can be
/// used with popups and the Windows on-screen keyboard as a fallback input surface.
/// </summary>
public sealed class AppControllerRuntimeService
{
    private const int AnalogDeadzone = 9000;
    private const int MaximumCursorStep = 18;

    public bool Execute(AppControllerOutput output, Func<bool>? focusManagedApp = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        switch (output.Kind)
        {
            case AppControllerOutputKind.None:
                return false;
            case AppControllerOutputKind.KeyboardShortcut:
                if (string.IsNullOrWhiteSpace(output.Value)) return false;
                if (focusManagedApp is not null && !focusManagedApp()) return false;
                SendShortcut(output.Value);
                return true;
            case AppControllerOutputKind.GrevKeyboard:
                if (focusManagedApp is not null && !focusManagedApp()) return false;
                OpenOnScreenKeyboard();
                return true;
            case AppControllerOutputKind.MouseLeftClick:
                SendMouseButton(MouseEventFlags.LeftDown, MouseEventFlags.LeftUp);
                return true;
            case AppControllerOutputKind.MouseRightClick:
                SendMouseButton(MouseEventFlags.RightDown, MouseEventFlags.RightUp);
                return true;
            case AppControllerOutputKind.MediaCommand:
                return SendMediaCommand(output.Value);
            case AppControllerOutputKind.MouseCursor:
            case AppControllerOutputKind.MouseScroll:
                // Continuous analog outputs are handled by ExecuteAnalog.
                return false;
            default:
                return false;
        }
    }

    public bool ExecuteAnalog(AppControllerOutput output, short x, short y)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (Math.Abs((int)x) < AnalogDeadzone && Math.Abs((int)y) < AnalogDeadzone)
        {
            return false;
        }

        return output.Kind switch
        {
            AppControllerOutputKind.MouseCursor => MoveCursor(x, y),
            AppControllerOutputKind.MouseScroll => Scroll(y),
            _ => false
        };
    }

    private static void SendShortcut(string shortcut)
    {
        var tokens = shortcut
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToUpperInvariant())
            .ToArray();
        if (tokens.Length == 0) return;

        var keys = tokens.Select(ParseVirtualKey).ToArray();
        foreach (var key in keys)
        {
            SendKeyboard(key, keyUp: false);
        }
        for (var index = keys.Length - 1; index >= 0; index--)
        {
            SendKeyboard(keys[index], keyUp: true);
        }
    }

    private static ushort ParseVirtualKey(string token) => token switch
    {
        "CTRL" or "CONTROL" => 0x11,
        "SHIFT" => 0x10,
        "ALT" => 0x12,
        "ENTER" or "RETURN" => 0x0D,
        "ESC" or "ESCAPE" => 0x1B,
        "TAB" => 0x09,
        "SPACE" => 0x20,
        "UP" => 0x26,
        "DOWN" => 0x28,
        "LEFT" => 0x25,
        "RIGHT" => 0x27,
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        "," => 0xBC,
        "/" => 0xBF,
        _ when token.Length == 1 && token[0] is >= 'A' and <= 'Z' => token[0],
        _ when token.Length == 1 && token[0] is >= '0' and <= '9' => token[0],
        _ => throw new InvalidOperationException($"Unsupported controller-profile keyboard token '{token}'.")
    };

    private static void SendKeyboard(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = InputType.Keyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? KeyboardEventFlags.KeyUp : 0
                }
            }
        };

        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 0)
        {
            throw new InvalidOperationException("Windows did not accept the Grev controller keyboard input.");
        }
    }

    private static void SendMouseButton(MouseEventFlags down, MouseEventFlags up)
    {
        var inputs = new[]
        {
            new INPUT
            {
                type = InputType.Mouse,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = down } }
            },
            new INPUT
            {
                type = InputType.Mouse,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = up } }
            }
        };

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 0)
        {
            throw new InvalidOperationException("Windows did not accept the Grev controller mouse input.");
        }
    }

    private static bool MoveCursor(short x, short y)
    {
        if (!GetCursorPos(out var point)) return false;
        var dx = ScaleAxis(x);
        var dy = -ScaleAxis(y);
        if (dx == 0 && dy == 0) return false;
        return SetCursorPos(point.X + dx, point.Y + dy);
    }

    private static int ScaleAxis(short value)
    {
        var absolute = Math.Abs((int)value);
        if (absolute < AnalogDeadzone) return 0;
        var normalized = (absolute - AnalogDeadzone) / (32767d - AnalogDeadzone);
        var step = Math.Max(1, (int)Math.Round(normalized * MaximumCursorStep));
        return value < 0 ? -step : step;
    }

    private static bool Scroll(short y)
    {
        if (Math.Abs((int)y) < AnalogDeadzone) return false;
        var wheel = y > 0 ? 120 : -120;
        var input = new INPUT
        {
            type = InputType.Mouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)wheel),
                    dwFlags = MouseEventFlags.Wheel
                }
            }
        };
        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 0;
    }

    private static bool SendMediaCommand(string? command)
    {
        var key = command?.ToUpperInvariant() switch
        {
            "PLAY_PAUSE" => (ushort)0xB3,
            "PREVIOUS" => (ushort)0xB1,
            "NEXT" => (ushort)0xB0,
            _ => (ushort)0
        };
        if (key == 0) return false;
        SendKeyboard(key, keyUp: false);
        SendKeyboard(key, keyUp: true);
        return true;
    }

    private static void OpenOnScreenKeyboard()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var osk = Path.Combine(windows, "System32", "osk.exe");
        if (!File.Exists(osk))
        {
            throw new FileNotFoundException("Windows On-Screen Keyboard was not found.", osk);
        }

        if (Process.GetProcessesByName("osk").Length > 0)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = osk,
            UseShellExecute = true
        });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1
    }

    [Flags]
    private enum KeyboardEventFlags : uint
    {
        KeyUp = 0x0002
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        Wheel = 0x0800
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public InputType type;
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
        public MouseEventFlags dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KeyboardEventFlags dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
