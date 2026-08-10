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
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort AButton = 0x1000;
    private const ushort BButton = 0x2000;
    private const ushort XButton = 0x4000;
    private const ushort YButton = 0x8000;
    private const int ThumbDeadzone = 14500;

    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(115);

    private readonly ControllerShortcutService _shortcutService;
    private readonly Timer _timer;
    private readonly ushort[] _previousButtons = new ushort[4];
    private readonly bool[] _connected = new bool[4];
    private readonly InputAction?[] _heldDirection = new InputAction?[4];
    private readonly DateTimeOffset[] _heldDirectionStarted = new DateTimeOffset[4];
    private readonly DateTimeOffset[] _lastDirectionRaised = new DateTimeOffset[4];
    private readonly Dictionary<(int ControllerIndex, string BindingId), ShortcutPressState> _shortcutStates = new();
    private readonly object _pollGate = new();
    private IReadOnlyList<ControllerShortcutBinding> _shortcutBindings = Array.Empty<ControllerShortcutBinding>();
    private bool _disposed;

    public event Action<ControllerInputEventArgs>? ActionPressed;
    public event Action<ControllerConnectionEventArgs>? ConnectionChanged;
    public event Action<ControllerShortcutEventArgs>? ShortcutRequested;

    public ControllerInputService(ControllerShortcutService shortcutService)
    {
        _shortcutService = shortcutService;
        _timer = new Timer(Poll, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        ReloadShortcuts();
    }

    public void ReloadShortcuts()
    {
        lock (_pollGate)
        {
            _shortcutBindings = _shortcutService
                .LoadOrCreate()
                .Bindings
                .Where(binding => binding.Enabled)
                .ToArray();
            _shortcutStates.Clear();
        }
    }

    public void Start()
    {
        if (!_disposed)
        {
            _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(33));
        }
    }

    private void Poll(object? stateObject)
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
                var shortcutActive = HandleShortcuts(index, state.Gamepad);

                if (!shortcutActive)
                {
                    if (WasPressed(index, buttons, AButton))
                    {
                        Raise(index, InputAction.Accept);
                    }

                    if (WasPressed(index, buttons, BButton))
                    {
                        Raise(index, InputAction.Back);
                    }

                    HandleDirectionalInput(index, GetDirectionalAction(buttons, state.Gamepad));
                }
                else
                {
                    // A configured system chord must not also navigate/activate the foreground UI.
                    HandleDirectionalInput(index, null);
                }

                _previousButtons[index] = buttons;
            }
        }
        finally
        {
            Monitor.Exit(_pollGate);
        }
    }

    private bool HandleShortcuts(int controllerIndex, XInputGamepad gamepad)
    {
        var selected = _shortcutBindings
            .Where(binding => IsBindingPressed(binding, gamepad))
            .OrderByDescending(binding => binding.Buttons.Count)
            .FirstOrDefault();

        foreach (var key in _shortcutStates.Keys
                     .Where(key => key.ControllerIndex == controllerIndex && key.BindingId != selected?.Id)
                     .ToArray())
        {
            _shortcutStates.Remove(key);
        }

        if (selected is null)
        {
            return false;
        }

        var stateKey = (controllerIndex, selected.Id);
        if (!_shortcutStates.TryGetValue(stateKey, out var pressState))
        {
            pressState = new ShortcutPressState(DateTimeOffset.UtcNow);
            _shortcutStates[stateKey] = pressState;
        }

        if (!pressState.Raised &&
            DateTimeOffset.UtcNow - pressState.StartedAt >= TimeSpan.FromMilliseconds(selected.HoldMilliseconds))
        {
            pressState.Raised = true;
            ShortcutRequested?.Invoke(new ControllerShortcutEventArgs(
                controllerIndex,
                selected.Action,
                selected.Id));
        }

        return true;
    }

    private static bool IsBindingPressed(ControllerShortcutBinding binding, XInputGamepad gamepad) =>
        binding.Buttons.All(button => IsButtonPressed(button, gamepad, binding.TriggerThreshold));

    private static bool IsButtonPressed(ControllerButton button, XInputGamepad gamepad, byte triggerThreshold) =>
        button switch
        {
            ControllerButton.DPadUp => HasButton(gamepad.Buttons, DPadUp),
            ControllerButton.DPadDown => HasButton(gamepad.Buttons, DPadDown),
            ControllerButton.DPadLeft => HasButton(gamepad.Buttons, DPadLeft),
            ControllerButton.DPadRight => HasButton(gamepad.Buttons, DPadRight),
            ControllerButton.Menu => HasButton(gamepad.Buttons, StartButton),
            ControllerButton.View => HasButton(gamepad.Buttons, BackButton),
            ControllerButton.LeftThumb => HasButton(gamepad.Buttons, LeftThumb),
            ControllerButton.RightThumb => HasButton(gamepad.Buttons, RightThumb),
            ControllerButton.LeftShoulder => HasButton(gamepad.Buttons, LeftShoulder),
            ControllerButton.RightShoulder => HasButton(gamepad.Buttons, RightShoulder),
            ControllerButton.A => HasButton(gamepad.Buttons, AButton),
            ControllerButton.B => HasButton(gamepad.Buttons, BButton),
            ControllerButton.X => HasButton(gamepad.Buttons, XButton),
            ControllerButton.Y => HasButton(gamepad.Buttons, YButton),
            ControllerButton.LeftTrigger => gamepad.LeftTrigger >= triggerThreshold,
            ControllerButton.RightTrigger => gamepad.RightTrigger >= triggerThreshold,
            _ => false
        };

    private static bool HasButton(ushort currentButtons, ushort button) =>
        (currentButtons & button) != 0;

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

        foreach (var key in _shortcutStates.Keys.Where(key => key.ControllerIndex == index).ToArray())
        {
            _shortcutStates.Remove(key);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private sealed class ShortcutPressState
    {
        public DateTimeOffset StartedAt { get; }
        public bool Raised { get; set; }

        public ShortcutPressState(DateTimeOffset startedAt)
        {
            StartedAt = startedAt;
        }
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
