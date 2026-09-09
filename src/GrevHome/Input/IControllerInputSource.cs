namespace GrevHome.Input;

/// <summary>Shell-facing input contract. The current implementation is four-slot XInput.</summary>
public interface IControllerInputSource : IDisposable
{
    event Action<ControllerInputEventArgs>? ActionPressed;
    event Action<int>? AcceptLongPressed;
    event Action<int>? AcceptReleased;
    event Action<int>? AcceptCancelled;
    event Action<ControllerConnectionEventArgs>? ConnectionChanged;
    event Action<ControllerShortcutEventArgs>? ShortcutRequested;
    event Action<ControllerShortcutCaptureEventArgs>? ShortcutCaptured;
    event Action? ShortcutCaptureTimedOut;
    event Action<ControllerAppControlEventArgs>? AppControlPressed;
    event Action<ControllerAnalogEventArgs>? AnalogChanged;
    bool AppInputMode { get; set; }
    bool IsCapturingShortcut { get; }
    void ReloadShortcuts();
    void BeginShortcutCapture();
    void CancelShortcutCapture();
    void Start();
    void PulseVibration(int controllerIndex, ushort strength, int durationMilliseconds);
}
