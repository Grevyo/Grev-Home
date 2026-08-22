using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GrevHome;

public partial class MainWindow
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;

    private HwndSource? _applianceWindowSource;
    private DateTimeOffset _lastResumeRefreshAt = DateTimeOffset.MinValue;
    private bool _applianceLifecycleReady;

    private void InitializeApplianceLifecycleIntegration()
    {
        if (_applianceLifecycleReady)
        {
            return;
        }

        _applianceLifecycleReady = true;
        EnsureShellPresentation();

        var handle = new WindowInteropHelper(this).Handle;
        _applianceWindowSource = HwndSource.FromHwnd(handle);
        _applianceWindowSource?.AddHook(ApplianceWindowProc);

        Closed += (_, _) =>
        {
            _applianceWindowSource?.RemoveHook(ApplianceWindowProc);
            _applianceWindowSource = null;
        };
    }

    private IntPtr ApplianceWindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmPowerBroadcast)
        {
            return IntPtr.Zero;
        }

        var powerEvent = wParam.ToInt32();
        if (powerEvent == PbtApmSuspend)
        {
            // Stamp every managed runtime before Windows suspends the process. Runtime recovery
            // persists this boundary so sleep can never be mistaken for active playtime/XP.
            _runtimeSessions.NotifySystemSuspend(DateTimeOffset.UtcNow);
        }
        else if (powerEvent is PbtApmResumeSuspend or PbtApmResumeAutomatic)
        {
            Dispatcher.BeginInvoke(new Action(HandleWindowsResume));
        }

        return IntPtr.Zero;
    }

    private void HandleWindowsResume()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastResumeRefreshAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastResumeRefreshAt = now;
        _runtimeSessions.NotifySystemResume(now);

        // XInput polling and runtime monitors remain alive across sleep. Refresh configuration and
        // shell surfaces immediately so the UI does not wait for a later navigation event.
        _controllerInput.ReloadShortcuts();
        RefreshSessionSurfaces();
        UpdateRuntimeSurfaces();

        // If a tracked external app owns the console surface, MainWindow is deliberately hidden.
        // Resume must not steal foreground from it. Only repair presentation when the shell itself
        // was already visible before/after Windows resumed.
        if (!IsVisible)
        {
            return;
        }

        EnsureShellPresentation();
        Activate();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (!IsStoreModalOpen && !IsPowerMenuOpen && !_overlayWindow.IsOpen)
                {
                    FocusFirstButton();
                }
            }));
    }

    private void EnsureShellPresentation()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        if (WindowState != WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }
}
