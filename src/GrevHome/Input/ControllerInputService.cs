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
public sealed record ControllerShortcutCaptureEventArgs(int ControllerIndex, IReadOnlyList<ControllerButton> Buttons);
public sealed record ControllerAppControlEventArgs(int ControllerIndex, AppControllerControl Control);
public sealed record ControllerAnalogEventArgs(
    int ControllerIndex,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY);

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
    private const byte CaptureTriggerThreshold = 160;

    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(115);
    private static readonly TimeSpan AcceptLongPressDelay = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);

    private readonly ControllerShortcutService _shortcutService;
    private readonly Timer _timer;
    private readonly ushort[] _previousButtons = new ushort[4];
    private readonly byte[] _previousLeftTriggers = new byte[4];
    private readonly byte[] _previousRightTriggers = new byte[4];
    private readonly bool[] _connected = new bool[4];
    private readonly InputAction?[] _heldDirection = new InputAction?[4];
    private readonly DateTimeOffset[] _heldDirectionStarted = new DateTimeOffset[4];
    private readonly DateTimeOffset[] _lastDirectionRaised = new DateTimeOffset[4];
    private readonly bool[] _acceptTracking = new bool[4];
    private readonly bool[] _acceptLongPressRaised = new bool[4];
    private readonly DateTimeOffset[] _acceptStarted = new DateTimeOffset[4];
    private readonly Dictionary<(int ControllerIndex, string BindingId), ShortcutPressState> _shortcutStates = new();
    private readonly object _pollGate = new();
    private IReadOnlyList<ControllerShortcutBinding> _shortcutBindings = Array.Empty<ControllerShortcutBinding>();
    private ShortcutCaptureState? _capture;
    private volatile bool _appInputMode;
    private bool _disposed;

    public event Action<ControllerInputEventArgs>? ActionPressed;
    public event Action<int>? AcceptLongPressed;
    public event Action<int>? AcceptReleased;
    public event Action<int>? AcceptCancelled;
    public event Action<ControllerConnectionEventArgs>? ConnectionChanged;
    public event Action<ControllerShortcutEventArgs>? ShortcutRequested;
    public event Action<ControllerShortcutCaptureEventArgs>? ShortcutCaptured;
    public event Action? ShortcutCaptureTimedOut;
    public event Action<ControllerAppControlEventArgs>? AppControlPressed;
    public event Action<ControllerAnalogEventArgs>? AnalogChanged;

    public ControllerInputService(ControllerShortcutService shortcutService)
    {
        _shortcutService = shortcutService;
        _timer = new Timer(Poll, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        ReloadShortcuts();
    }

    public bool AppInputMode
    {
        get => _appInputMode;
        set => _appInputMode = value;
    }

    public bool IsCapturingShortcut
    {
        get
        {
            lock (_pollGate)
            {
                return _capture is not null;
            }
        }
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

    public void BeginShortcutCapture()
    {
        lock (_pollGate)
        {
            _capture = new ShortcutCaptureState(DateTimeOffset.UtcNow);
            _shortcutStates.Clear();
            Array.Fill(_heldDirection, null);
            for (var index = 0; index < _acceptTracking.Length; index++)
            {
                CancelAcceptTracking(index);
            }
        }
    }

    public void CancelShortcutCapture()
    {
        lock (_pollGate)
        {
            _capture = null;
        }
    }

    public void Start()
    {
        if (!_disposed)
        {
            _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(33));
        }
    }

    public void PulseVibration(int controllerIndex, ushort strength, int durationMilliseconds)
    {
        if (_disposed || controllerIndex is < 0 or > 3) return;
        try
        {
            SetVibration((uint)controllerIndex, new XInputVibration
            {
                LeftMotorSpeed = strength,
                RightMotorSpeed = (ushort)(strength * 0.72)
            });
            _ = Task.Run(async () =>
            {
                await Task.Delay(durationMilliseconds);
                if (!_disposed) SetVibration((uint)controllerIndex, default);
            });
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private void Poll(object? stateObject)
    {
        if (_disposed || !Monitor.TryEnter(_pollGate))
        {
            return;
        }

        try
        {
            var states = new XInputState[4];
            for (var index = 0; index < 4; index++)
            {
                var isConnected = XInputGetState((uint)index, out states[index]) == 0;
                UpdateConnection(index, isConnected);

                if (!isConnected)
                {
                    ResetController(index);
                }
            }

            if (_capture is not null)
            {
                HandleShortcutCapture(states);
                return;
            }

            for (var index = 0; index < 4; index++)
            {
                if (!_connected[index])
                {
                    continue;
                }

                var state = states[index];
                var buttons = state.Gamepad.Buttons;
                var shortcutActive = HandleShortcuts(index, state.Gamepad);
                HandleAcceptHold(index, buttons, shortcutActive);

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

                    if (AppInputMode)
                    {
                        RaiseExtendedAppControls(index, state.Gamepad);
                        AnalogChanged?.Invoke(new ControllerAnalogEventArgs(
                            index,
                            state.Gamepad.ThumbLX,
                            state.Gamepad.ThumbLY,
                            state.Gamepad.ThumbRX,
                            state.Gamepad.ThumbRY));
                    }
                }
                else
                {
                    HandleDirectionalInput(index, null);
                }

                _previousButtons[index] = buttons;
                _previousLeftTriggers[index] = state.Gamepad.LeftTrigger;
                _previousRightTriggers[index] = state.Gamepad.RightTrigger;
            }
        }
        finally
        {
            Monitor.Exit(_pollGate);
        }
    }

    private void RaiseExtendedAppControls(int index, XInputGamepad gamepad)
    {
        RaiseAppIfPressed(index, gamepad.Buttons, XButton, AppControllerControl.X);
        RaiseAppIfPressed(index, gamepad.Buttons, YButton, AppControllerControl.Y);
        RaiseAppIfPressed(index, gamepad.Buttons, LeftShoulder, AppControllerControl.LeftShoulder);
        RaiseAppIfPressed(index, gamepad.Buttons, RightShoulder, AppControllerControl.RightShoulder);
        RaiseAppIfPressed(index, gamepad.Buttons, StartButton, AppControllerControl.Menu);
        RaiseAppIfPressed(index, gamepad.Buttons, BackButton, AppControllerControl.View);
        RaiseAppIfPressed(index, gamepad.Buttons, LeftThumb, AppControllerControl.LeftThumb);
        RaiseAppIfPressed(index, gamepad.Buttons, RightThumb, AppControllerControl.RightThumb);

        if (gamepad.LeftTrigger >= CaptureTriggerThreshold && _previousLeftTriggers[index] < CaptureTriggerThreshold)
        {
            AppControlPressed?.Invoke(new ControllerAppControlEventArgs(index, AppControllerControl.LeftTrigger));
        }

        if (gamepad.RightTrigger >= CaptureTriggerThreshold && _previousRightTriggers[index] < CaptureTriggerThreshold)
        {
            AppControlPressed?.Invoke(new ControllerAppControlEventArgs(index, AppControllerControl.RightTrigger));
        }
    }

    private void RaiseAppIfPressed(int index, ushort buttons, ushort mask, AppControllerControl control)
    {
        if (WasPressed(index, buttons, mask))
        {
            AppControlPressed?.Invoke(new ControllerAppControlEventArgs(index, control));
        }
    }

    private void HandleAcceptHold(int index, ushort buttons, bool shortcutActive)
    {
        if (shortcutActive)
        {
            if (_acceptTracking[index])
            {
                CancelAcceptTracking(index);
            }
            return;
        }

        var isDown = HasButton(buttons, AButton);
        if (!isDown)
        {
            if (!_acceptTracking[index])
            {
                return;
            }

            _acceptTracking[index] = false;
            _acceptLongPressRaised[index] = false;
            AcceptReleased?.Invoke(index);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!_acceptTracking[index])
        {
            _acceptTracking[index] = true;
            _acceptLongPressRaised[index] = false;
            _acceptStarted[index] = now;
            return;
        }

        if (!_acceptLongPressRaised[index] && now - _acceptStarted[index] >= AcceptLongPressDelay)
        {
            _acceptLongPressRaised[index] = true;
            AcceptLongPressed?.Invoke(index);
        }
    }

    private void CancelAcceptTracking(int index)
    {
        if (!_acceptTracking[index])
        {
            return;
        }

        _acceptTracking[index] = false;
        _acceptLongPressRaised[index] = false;
        AcceptCancelled?.Invoke(index);
    }

    private void HandleShortcutCapture(IReadOnlyList<XInputState> states)
    {
        var capture = _capture;
        if (capture is null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - capture.StartedAt >= CaptureTimeout)
        {
            _capture = null;
            ThreadPool.QueueUserWorkItem(_ => ShortcutCaptureTimedOut?.Invoke());
            return;
        }

        var pressedByController = Enumerable.Range(0, 4)
            .Where(index => _connected[index])
            .Select(index => (Index: index, Buttons: GetPressedButtons(states[index].Gamepad, CaptureTriggerThreshold)))
            .ToArray();

        if (!capture.NeutralSeen)
        {
            if (pressedByController.All(item => item.Buttons.Count == 0))
            {
                capture.NeutralSeen = true;
            }

            return;
        }

        if (capture.ControllerIndex is null)
        {
            var firstActive = pressedByController.FirstOrDefault(item => item.Buttons.Count > 0);
            if (firstActive.Buttons is null || firstActive.Buttons.Count == 0)
            {
                return;
            }

            capture.ControllerIndex = firstActive.Index;
            capture.LargestCombination = firstActive.Buttons;
            return;
        }

        var controller = pressedByController.FirstOrDefault(item => item.Index == capture.ControllerIndex.Value);
        var currentlyPressed = controller.Buttons ?? Array.Empty<ControllerButton>();

        if (currentlyPressed.Count > capture.LargestCombination.Count)
        {
            capture.LargestCombination = currentlyPressed;
        }

        if (currentlyPressed.Count > 0)
        {
            return;
        }

        var completedController = capture.ControllerIndex.Value;
        var completedButtons = capture.LargestCombination.Distinct().Take(8).ToArray();
        _capture = null;

        if (completedButtons.Length > 0)
        {
            ThreadPool.QueueUserWorkItem(_ => ShortcutCaptured?.Invoke(
                new ControllerShortcutCaptureEventArgs(completedController, completedButtons)));
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

    private static IReadOnlyList<ControllerButton> GetPressedButtons(XInputGamepad gamepad, byte triggerThreshold)
    {
        var result = new List<ControllerButton>(16);
        foreach (var button in Enum.GetValues<ControllerButton>())
        {
            if (IsButtonPressed(button, gamepad, triggerThreshold))
            {
                result.Add(button);
            }
        }

        return result;
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

    private void Raise(int index, InputAction action)
    {
        if (!AppInputMode)
        {
            ActionPressed?.Invoke(new ControllerInputEventArgs(index, action));
            return;
        }

        var control = action switch
        {
            InputAction.Up => AppControllerControl.DPadUp,
            InputAction.Down => AppControllerControl.DPadDown,
            InputAction.Left => AppControllerControl.DPadLeft,
            InputAction.Right => AppControllerControl.DPadRight,
            InputAction.Accept => AppControllerControl.A,
            InputAction.Back => AppControllerControl.B,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        AppControlPressed?.Invoke(new ControllerAppControlEventArgs(index, control));
    }

    private void ResetController(int index)
    {
        _previousButtons[index] = 0;
        _previousLeftTriggers[index] = 0;
        _previousRightTriggers[index] = 0;
        _heldDirection[index] = null;
        CancelAcceptTracking(index);

        foreach (var key in _shortcutStates.Keys.Where(key => key.ControllerIndex == index).ToArray())
        {
            _shortcutStates.Remove(key);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        for (uint index = 0; index < 4; index++)
        {
            try { SetVibration(index, default); } catch { }
        }
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

    private sealed class ShortcutCaptureState
    {
        public DateTimeOffset StartedAt { get; }
        public bool NeutralSeen { get; set; }
        public int? ControllerIndex { get; set; }
        public IReadOnlyList<ControllerButton> LargestCombination { get; set; } = Array.Empty<ControllerButton>();

        public ShortcutCaptureState(DateTimeOffset startedAt)
        {
            StartedAt = startedAt;
        }
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint XInputSetState(uint userIndex, ref XInputVibration vibration);

    private static uint SetVibration(uint userIndex, XInputVibration vibration) =>
        XInputSetState(userIndex, ref vibration);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

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
