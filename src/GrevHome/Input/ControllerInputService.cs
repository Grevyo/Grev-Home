using System.Runtime.InteropServices;

namespace GrevHome.Input;

public enum InputAction
{
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back
}

public sealed record ControllerInputEventArgs(int ControllerIndex, InputAction Action);
public sealed record ControllerConnectionEventArgs(int ControllerIndex, bool IsConnected);

public sealed class ControllerInputService : IDisposable
{
    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort StartButton = 0x0010;
    private const ushort BackButton = 0x0020;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort AButton = 0x1000;
    private const ushort BButton = 0x2000;
    private const int ThumbDeadzone = 14500;

    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(115);
    private static readonly TimeSpan HomeShortcutHold = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan OverlayShortcutHold = TimeSpan.FromMilliseconds(450);

    private readonly Timer _timer;
    private readonly ushort[] _previousButtons = new ushort[4];
    private readonly bool[] _connected = new bool[4];
    private readonly InputAction?[] _heldDirection = new InputAction?[4];
    private readonly DateTimeOffset[] _heldDirectionStarted = new DateTimeOffset[4];
    private readonly DateTimeOffset[] _lastDirectionRaised = new DateTimeOffset[4];
    private readonly DateTimeOffset?[] _homeShortcutStarted = new DateTimeOffset?[4];
    private readonly bool[] _homeShortcutRaised = new bool[4];
    private readonly DateTimeOffset?[] _overlayShortcutStarted = new DateTimeOffset?[4];
    private readonly bool[] _overlayShortcutRaised = new bool[4];
    private readonly object _pollGate = new();
    private bool _disposed;

    public event Action<ControllerInputEventArgs>? ActionPressed;
    public event Action<ControllerConnectionEventArgs>? ConnectionChanged;
    public event Action<int>? ReturnHomeRequested;
    public event Action<int>? OverlayRequested;

    public ControllerInputService()
    {
        _timer = new Timer(Poll, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        if (!_disposed)
        {
            _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(33));
        }
    }

    private void Poll(object? _)
    {
        if (_disposed || !Monitor.TryEnter(_pollGate))
        {
            return;
        }

        try
        {
            for (var index = 0; index < 4; index++)
            {
                var isConnected = XInputGetState((uint)index, out var state) == 0;
                UpdateConnection(index, isConnected);

                if (!isConnected)
                {
                    ResetController(index);
                    continue;
                }

                var buttons = state.Gamepad.Buttons;
                var homeComboActive = HasReturnHomeCombo(buttons);
                var overlayComboActive = HasOverlayCombo(buttons);
                HandleReturnHomeShortcut(index, homeComboActive);
                HandleOverlayShortcut(index, overlayComboActive);

                if (!homeComboActive && !overlayComboActive)
                {
                    if (WasPressed(index, buttons, AButton))
                    {
                        Raise(index, InputAction.Accept);
                    }

                    if (WasPressed(index, buttons, BButton))
                    {
                        Raise(index, InputAction.Back);
                    }
                }

                HandleDirectionalInput(index, GetDirectionalAction(buttons, state.Gamepad));
                _previousButtons[index] = buttons;
            }
        }
        finally
        {
            Monitor.Exit(_pollGate);
        }
    }

    private void UpdateConnection(int index, bool isConnected)
    {
        if (_connected[index] == isConnected)
        {
            return;
        }

        _connected[index] = isConnected;
        ConnectionChanged?.Invoke(new ControllerConnectionEventArgs(index, isConnected));
    }

    private void HandleDirectionalInput(int index, InputAction? direction)
    {
        if (direction is null)
        {
            _heldDirection[index] = null;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_heldDirection[index] != direction)
        {
            _heldDirection[index] = direction;
            _heldDirectionStarted[index] = now;
            _lastDirectionRaised[index] = now;
            Raise(index, direction.Value);
            return;
        }

        if (now - _heldDirectionStarted[index] < RepeatDelay ||
            now - _lastDirectionRaised[index] < RepeatInterval)
        {
            return;
        }

        _lastDirectionRaised[index] = now;
        Raise(index, direction.Value);
    }

    private void HandleReturnHomeShortcut(int index, bool active)
    {
        if (!active)
        {
            _homeShortcutStarted[index] = null;
            _homeShortcutRaised[index] = false;
            return;
        }

        _homeShortcutStarted[index] ??= DateTimeOffset.UtcNow;
        if (_homeShortcutRaised[index] ||
            DateTimeOffset.UtcNow - _homeShortcutStarted[index] < HomeShortcutHold)
        {
            return;
        }

        _homeShortcutRaised[index] = true;
        ReturnHomeRequested?.Invoke(index);
    }

    private void HandleOverlayShortcut(int index, bool active)
    {
        if (!active)
        {
            _overlayShortcutStarted[index] = null;
            _overlayShortcutRaised[index] = false;
            return;
        }

        _overlayShortcutStarted[index] ??= DateTimeOffset.UtcNow;
        if (_overlayShortcutRaised[index] ||
            DateTimeOffset.UtcNow - _overlayShortcutStarted[index] < OverlayShortcutHold)
        {
            return;
        }

        _overlayShortcutRaised[index] = true;
        OverlayRequested?.Invoke(index);
    }

    private static bool HasReturnHomeCombo(ushort buttons) =>
        (buttons & LeftShoulder) != 0 &&
        (buttons & RightShoulder) != 0 &&
        (buttons & BackButton) != 0;

    private static bool HasOverlayCombo(ushort buttons) =>
        (buttons & LeftShoulder) != 0 &&
        (buttons & RightShoulder) != 0 &&
        (buttons & StartButton) != 0;

    private bool WasPressed(int index, ushort currentButtons, ushort button)
    {
        var isDown = (currentButtons & button) != 0;
        var wasDown = (_previousButtons[index] & button) != 0;
        return isDown && !wasDown;
    }

    private static InputAction? GetDirectionalAction(ushort buttons, XInputGamepad gamepad)
    {
        if ((buttons & DPadUp) != 0) return InputAction.Up;
        if ((buttons & DPadDown) != 0) return InputAction.Down;
        if ((buttons & DPadLeft) != 0) return InputAction.Left;
        if ((buttons & DPadRight) != 0) return InputAction.Right;

        var x = (int)gamepad.ThumbLX;
        var y = (int)gamepad.ThumbLY;
        var absoluteX = Math.Abs(x);
        var absoluteY = Math.Abs(y);

        if (absoluteX < ThumbDeadzone && absoluteY < ThumbDeadzone)
        {
            return null;
        }

        if (absoluteX > absoluteY)
        {
            return x < 0 ? InputAction.Left : InputAction.Right;
        }

        return y < 0 ? InputAction.Down : InputAction.Up;
    }

    private void Raise(int index, InputAction action) =>
        ActionPressed?.Invoke(new ControllerInputEventArgs(index, action));

    private void ResetController(int index)
    {
        _previousButtons[index] = 0;
        _heldDirection[index] = null;
        _homeShortcutStarted[index] = null;
        _homeShortcutRaised[index] = false;
        _overlayShortcutStarted[index] = null;
        _overlayShortcutRaised[index] = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }
}
