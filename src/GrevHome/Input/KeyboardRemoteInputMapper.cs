using System.Windows.Input;

namespace GrevHome.Input;

/// <summary>Shared shell mapping for keyboard-emulating USB/Bluetooth remotes.</summary>
public static class KeyboardRemoteInputMapper
{
    public static InputAction? Map(Key key) => key switch
    {
        Key.Up => InputAction.Up,
        Key.Down => InputAction.Down,
        Key.Left or Key.MediaPreviousTrack => InputAction.Left,
        Key.Right or Key.MediaNextTrack or Key.BrowserForward => InputAction.Right,
        Key.Enter or Key.Space or Key.Select or Key.MediaPlayPause => InputAction.Accept,
        Key.Escape or Key.BrowserBack or Key.MediaStop => InputAction.Back,
        _ => null
    };
}
