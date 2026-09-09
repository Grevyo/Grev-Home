using System.Runtime.InteropServices;
using System.Windows;

namespace GrevHome;

public partial class MainWindow
{
    private const int OverlaySwMinimize = 6;
    private bool _overlayControllerOwnershipReady;
    private Guid? _overlayMinimizedLauncherSessionId;

    private void InitializeOverlayControllerOwnership()
    {
        if (_overlayControllerOwnershipReady)
        {
            return;
        }

        _overlayControllerOwnershipReady = true;
        _overlayWindow.IsVisibleChanged += (_, _) => HandleOverlayVisibilityForControllerOwnership();
    }

    private void HandleOverlayVisibilityForControllerOwnership()
    {
        if (_overlayWindow.IsVisible)
        {
            // The overlay is the controller owner while visible. Stop Grev's app keyboard/mouse
            // translation completely so no synthetic app command can leak under the overlay.
            _controllerInput.AppInputMode = false;
            StopForegroundWindowWatch();
            MinimizeNativeControllerLauncherForOverlay();
            return;
        }

        _overlayMinimizedLauncherSessionId = null;
        if (!IsVisible && _foregroundLaunchSessionId is Guid launchSessionId)
        {
            // Resume/Switch will activate the managed app window through RuntimeSessionManager.
            // Re-enable its resolved controller profile only after the overlay is gone.
            UpdateForegroundAppInputMode();
            StartForegroundWindowWatch(launchSessionId);
        }
    }

    private void MinimizeNativeControllerLauncherForOverlay()
    {
        var foreground = _runtimeSessions.GetForegroundSession();
        if (foreground is null && _foregroundLaunchSessionId is Guid launchSessionId)
        {
            foreground = _runtimeSessions.GetSession(launchSessionId);
        }

        if (foreground is null ||
            !string.Equals(foreground.AppId, "steam", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var windows = _appProcessWindows.GetTopLevelWindows(foreground.ProcessIds);
        var minimizedAny = false;
        foreach (var window in windows)
        {
            minimizedAny |= ShowWindow(window.Handle, OverlaySwMinimize);
        }

        if (minimizedAny)
        {
            _overlayMinimizedLauncherSessionId = foreground.LaunchSessionId;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
