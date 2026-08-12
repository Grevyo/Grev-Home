using System.Windows;
using GrevHome.Input;
using GrevHome.Runtime;
using GrevHome.Store;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private static readonly TimeSpan ForegroundWindowPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ForegroundWindowHiddenGrace = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan GameLauncherWindowHiddenGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ManagedCloseEscalationDelay = TimeSpan.FromSeconds(3);

    private readonly AppControllerRuntimeService _appControllerRuntime = new();
    private readonly ProcessWindowService _appProcessWindows = new();
    private readonly ProcessTreeService _appProcessTree = new();
    private readonly HashSet<Guid> _controllerGuideShownSessions = [];
    private readonly HashSet<Guid> _scheduledCloseEscalations = [];
    private readonly object _closeEscalationGate = new();
    private ResolvedAppControllerProfile? _foregroundAppControllerProfile;
    private AppControllerGuidePreferenceService? _appControllerGuidePreferences;
    private Guid? _foregroundControllerProfileSessionId;
    private CancellationTokenSource? _foregroundWindowWatchCts;
    private bool _appControllerRuntimeIntegrationReady;

    private void InitializeAppControllerRuntimeIntegration()
    {
        if (_appControllerRuntimeIntegrationReady) return;
        _appControllerRuntimeIntegrationReady = true;
        _appControllerGuidePreferences = new AppControllerGuidePreferenceService(_paths);

        _controllerInput.AppControlPressed += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleForegroundAppControl(input)));
        _controllerInput.AnalogChanged += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleForegroundAppAnalog(input)));
        IsVisibleChanged += (_, _) => HandleShellVisibilityChanged();

        // Explicit user Close requests must end the managed session even when an app interprets
        // WM_CLOSE as "hide to tray" (Discord/Steam can do this). Give the app a short graceful
        // window, then terminate only the same Grev-tracked process identities if they remain.
        // Restart is intentionally not wired here; it keeps its own longer graceful/recovery timing.
        _runningAppsView.CloseRequested += ScheduleManagedCloseEscalation;
        _appKillerView.CloseRequested += ScheduleManagedCloseEscalation;
        _installedLibraryView.CloseRequested += ScheduleManagedCloseEscalation;
        _overlayWindow.CloseRequested += ScheduleManagedCloseEscalation;

        _overlayWindow.ControllerGuideDontShowAgainRequested += (grevId, appId) =>
        {
            try
            {
                _appControllerGuidePreferences.DisableForProfile(grevId, appId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _installedLibraryView.ShowStatus($"Could not save app-onboarding preference: {ex.Message}");
            }
        };

        _overlayWindow.ControllerGuideDisableControllerProfileRequested += (grevId, appId) =>
            _ = DisableAppControllerProfileFromGuideAsync(grevId, appId);

        _runtimeSessions.SessionEnded += snapshot =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _controllerGuideShownSessions.Remove(snapshot.LaunchSessionId);
                if (_foregroundControllerProfileSessionId == snapshot.LaunchSessionId)
                {
                    ClearForegroundAppControllerProfile();
                }
            }));
        };
    }

    private void HandleShellVisibilityChanged()
    {
        UpdateForegroundAppInputMode();

        if (IsVisible || !_foregroundLaunchSessionId.HasValue)
        {
            StopForegroundWindowWatch();
            return;
        }

        var launchSessionId = _foregroundLaunchSessionId.Value;
        RequestAppActivationUri(launchSessionId);
        StartForegroundWindowWatch(launchSessionId);
        _ = EnsureForegroundAppActivatedAsync(launchSessionId);
    }

    private void RequestAppActivationUri(Guid launchSessionId)
    {
        var snapshot = _runtimeSessions.GetSession(launchSessionId);
        var package = snapshot is null ? null : _grevStoreCatalog.Find(snapshot.AppId);
        var configured = package?.App.Launch.ActivationUri;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri) || uri.IsFile)
        {
            _installedLibraryView.ShowStatus(
                $"{package!.Presentation.DisplayName} has an invalid activation URI. The app process remains tracked.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _installedLibraryView.ShowStatus(
                $"Could not request {package!.Presentation.DisplayName}'s launch surface: {ex.Message}");
        }
    }

    private async Task EnsureForegroundAppActivatedAsync(Guid launchSessionId)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            if (IsVisible || _foregroundLaunchSessionId != launchSessionId)
            {
                return;
            }

            if (TryActivateManagedAppWindow(launchSessionId))
            {
                await MaybeOpenControllerGuideAsync(launchSessionId);
                return;
            }

            await Task.Delay(125);
        }

        if (IsVisible || _foregroundLaunchSessionId != launchSessionId)
        {
            return;
        }

        _foregroundLaunchSessionId = null;
        RestoreWindowWithoutChangingRoute();
        const string message = "The app process started, but Grev Home could not find a usable app window to bring forward. The app remains tracked in Running Apps/App Killer if its process is still active.";
        _installedLibraryView.ShowLaunchError(message);
        _grevStoreAppView.ShowStatus(message);
    }

    private bool TryActivateManagedAppWindow(Guid launchSessionId)
    {
        var snapshot = _runtimeSessions.GetSession(launchSessionId);
        if (snapshot is null || snapshot.ProcessIds.Count == 0)
        {
            return false;
        }

        var package = _grevStoreCatalog.Find(snapshot.AppId);
        var maximize = package?.EffectiveRuntimePolicy.WindowMode == AppWindowMode.Maximized;
        return _appProcessWindows.TryActivate(snapshot.ProcessIds, maximize);
    }

    private async Task MaybeOpenControllerGuideAsync(Guid launchSessionId)
    {
        if (_controllerGuideShownSessions.Contains(launchSessionId) ||
            IsVisible ||
            _foregroundLaunchSessionId != launchSessionId)
        {
            return;
        }

        var snapshot = _runtimeSessions.GetSession(launchSessionId);
        if (snapshot is null)
        {
            return;
        }

        var package = _grevStoreCatalog.Find(snapshot.AppId);
        var onboarding = package?.Onboarding;
        if (package is null ||
            !package.Supports(AppPackageCapability.ControllerGuide) ||
            onboarding is not { ShowOnFirstLaunch: true } ||
            onboarding.ControllerGuideControls.Count == 0)
        {
            return;
        }

        _appControllerGuidePreferences ??= new AppControllerGuidePreferenceService(_paths);
        if (!_appControllerGuidePreferences.ShouldShow(snapshot.PrimaryGrevId, snapshot.AppId))
        {
            return;
        }

        var profileService = _appControllerProfileService ??= new AppControllerProfileService(_paths);
        var resolved = string.IsNullOrWhiteSpace(snapshot.PrimaryGrevId)
            ? AppControllerProfileService.ResolveDefaults(package.ControllerProfile)
            : await profileService.ResolveAsync(snapshot.PrimaryGrevId, snapshot.AppId, package.ControllerProfile);

        if (!resolved.Enabled ||
            IsVisible ||
            _foregroundLaunchSessionId != launchSessionId ||
            _runtimeSessions.GetSession(launchSessionId) is null)
        {
            return;
        }

        var controls = onboarding.ControllerGuideControls
            .Take(12)
            .Select(control =>
            {
                var mapping = resolved.Mappings.FirstOrDefault(candidate => candidate.Control == control);
                var output = mapping?.Output ?? new AppControllerOutput(AppControllerOutputKind.None);
                return new ControllerGuideItem(
                    AppControllerProfileLayout.FormatControl(control),
                    AppControllerOutputCatalog.Format(output));
            })
            .ToArray();

        _controllerGuideShownSessions.Add(launchSessionId);
        _overlayWindow.OpenControllerGuide(
            snapshot.AppId,
            snapshot.PrimaryGrevId,
            onboarding.Title,
            onboarding.Summary,
            FormatSystemShortcut(ControllerShortcutAction.ReturnHome),
            FormatSystemShortcut(ControllerShortcutAction.Overlay),
            controls,
            onboarding.QuickDisableControllerProfileLabel,
            onboarding.QuickDisableControllerProfileDescription);
    }

    private string FormatSystemShortcut(ControllerShortcutAction action)
    {
        var bindings = _controllerShortcuts.LoadOrCreate().Bindings
            .Where(binding => binding.Enabled && binding.Action == action)
            .ToArray();
        if (bindings.Length == 0)
        {
            return "Not configured";
        }

        return string.Join(
            "   OR   ",
            bindings.Select(binding =>
                $"{SettingsView.FormatButtons(binding.Buttons)} • hold {binding.HoldMilliseconds} ms"));
    }

    private void ScheduleManagedCloseEscalation(Guid launchSessionId)
    {
        lock (_closeEscalationGate)
        {
            if (!_scheduledCloseEscalations.Add(launchSessionId))
            {
                return;
            }
        }

        _ = EscalateManagedCloseAsync(launchSessionId);
    }

    private async Task EscalateManagedCloseAsync(Guid launchSessionId)
    {
        try
        {
            await Task.Delay(ManagedCloseEscalationDelay);

            // If the session still exists, its explicit normal Close either failed or only hid
            // the app to its tray. ForceClose is identity-validated and scoped to this session.
            if (_runtimeSessions.GetSession(launchSessionId) is not null)
            {
                _runtimeSessions.ForceClose(launchSessionId);
            }
        }
        finally
        {
            lock (_closeEscalationGate)
            {
                _scheduledCloseEscalations.Remove(launchSessionId);
            }
        }
    }

    private void StartForegroundWindowWatch(Guid launchSessionId)
    {
        StopForegroundWindowWatch();
        _foregroundWindowWatchCts = new CancellationTokenSource();
        _ = MonitorForegroundWindowAsync(launchSessionId, _foregroundWindowWatchCts.Token);
    }

    private void StopForegroundWindowWatch()
    {
        var existing = _foregroundWindowWatchCts;
        _foregroundWindowWatchCts = null;
        if (existing is null)
        {
            return;
        }

        existing.Cancel();
        existing.Dispose();
    }

    private async Task MonitorForegroundWindowAsync(Guid launchSessionId, CancellationToken cancellationToken)
    {
        var observedVisibleWindow = false;
        DateTimeOffset? hiddenSince = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   !IsVisible &&
                   _foregroundLaunchSessionId == launchSessionId)
            {
                var snapshot = _runtimeSessions.GetSession(launchSessionId);
                if (snapshot is null)
                {
                    // Normal SessionEnded handling owns the process-exit return path.
                    return;
                }

                var package = _grevStoreCatalog.Find(snapshot.AppId);
                var returnBehavior = package?.EffectiveRuntimePolicy.ReturnBehavior
                                     ?? AppWindowReturnBehavior.ReturnHomeWhenMinimizedOrHidden;
                var state = _appProcessWindows.GetWindowState(snapshot.ProcessIds);
                if (state == RuntimeWindowState.Visible)
                {
                    observedVisibleWindow = true;
                    hiddenSince = null;
                }
                else if (returnBehavior == AppWindowReturnBehavior.KeepShellHidden)
                {
                    hiddenSince = null;
                }
                else if (observedVisibleWindow)
                {
                    // Game launchers are different from ordinary tray apps. When a launcher UI
                    // disappears because one of its descendants has taken foreground, keep Grev
                    // Home hidden so it never jumps over the game. If no launched child owns the
                    // foreground, treat the hidden/minimized launcher like Discord and return home.
                    if (package?.App.Kind == Apps.AppKind.GameLauncher &&
                        IsForegroundLauncherDescendant(snapshot.ProcessIds))
                    {
                        hiddenSince = null;
                    }
                    else
                    {
                        hiddenSince ??= DateTimeOffset.UtcNow;
                        var hiddenGrace = package?.App.Kind == Apps.AppKind.GameLauncher
                            ? GameLauncherWindowHiddenGrace
                            : ForegroundWindowHiddenGrace;
                        if (DateTimeOffset.UtcNow - hiddenSince.Value >= hiddenGrace)
                        {
                            _foregroundLaunchSessionId = null;
                            _overlayWindow.Dismiss();
                            RestoreWindowWithoutChangingRoute();

                            var reason = state == RuntimeWindowState.Minimized ? "minimized" : "hidden";
                            _installedLibraryView.ShowStatus(
                                $"{snapshot.AppName} is {reason} but still running. Use Running Apps or Switch to return to it.");
                            return;
                        }
                    }
                }

                await Task.Delay(ForegroundWindowPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when Grev Home becomes visible again or control moves to another app.
        }
    }

    private bool IsForegroundLauncherDescendant(IReadOnlyList<int> managedProcessIds)
    {
        var foregroundProcessId = _appProcessWindows.GetForegroundProcessId();
        if (!foregroundProcessId.HasValue || managedProcessIds.Contains(foregroundProcessId.Value))
        {
            return false;
        }

        return _appProcessTree.DiscoverDescendants(managedProcessIds)
            .Contains(foregroundProcessId.Value);
    }

    private void UpdateForegroundAppInputMode()
    {
        if (IsVisible || !_foregroundLaunchSessionId.HasValue)
        {
            _controllerInput.AppInputMode = false;
            ClearForegroundAppControllerProfile();
            return;
        }

        // External-app mode suppresses normal controller navigation of the hidden Grev Home
        // shell even when the selected app profile is disabled or still loading.
        _controllerInput.AppInputMode = true;
        _ = LoadForegroundAppControllerProfileAsync(_foregroundLaunchSessionId.Value);
    }

    private async Task LoadForegroundAppControllerProfileAsync(Guid launchSessionId)
    {
        var snapshot = _runtimeSessions.GetSession(launchSessionId);
        if (snapshot is null)
        {
            snapshot = _runtimeSessions.GetActiveSessions()
                .FirstOrDefault(session => session.LaunchSessionId == launchSessionId);
        }
        if (snapshot is null) return;

        var package = _grevStoreCatalog.Find(snapshot.AppId);
        var defaults = package?.ControllerProfile ?? AppControllerProfileDefaults.Empty;
        var service = _appControllerProfileService ??= new AppControllerProfileService(_paths);

        ResolvedAppControllerProfile resolved;
        if (string.IsNullOrWhiteSpace(snapshot.PrimaryGrevId))
        {
            resolved = AppControllerProfileService.ResolveDefaults(defaults);
        }
        else
        {
            resolved = await service.ResolveAsync(snapshot.PrimaryGrevId, snapshot.AppId, defaults);
        }

        if (IsVisible ||
            _foregroundLaunchSessionId != launchSessionId ||
            !_runtimeSessions.GetActiveSessions().Any(session => session.LaunchSessionId == launchSessionId))
        {
            return;
        }

        _foregroundControllerProfileSessionId = launchSessionId;
        _foregroundAppControllerProfile = resolved;
        _controllerInput.AppInputMode = true;
    }

    private void HandleForegroundAppControl(ControllerAppControlEventArgs input)
    {
        if (_overlayWindow.IsOpen)
        {
            // While an external app is active, A/B/D-pad/left-stick directions are emitted as
            // app controls instead of normal Grev Home actions. Route those controls back into
            // the visible Grev overlay so its controller navigation remains fully functional.
            var overlayAction = input.Control switch
            {
                AppControllerControl.DPadUp => InputAction.Up,
                AppControllerControl.DPadDown => InputAction.Down,
                AppControllerControl.DPadLeft => InputAction.Left,
                AppControllerControl.DPadRight => InputAction.Right,
                AppControllerControl.A => InputAction.Accept,
                AppControllerControl.B => InputAction.Back,
                _ => (InputAction?)null
            };

            if (overlayAction.HasValue)
            {
                _overlayWindow.HandleControllerInput(overlayAction.Value);
                return;
            }

            // Keep only the desktop pointer layer alive over Grev's overlay. Discord/app-specific
            // keyboard shortcuts remain blocked so they cannot fire into the app underneath.
            if (_controllerInput.AppInputMode)
            {
                var pointerOutput = input.Control switch
                {
                    AppControllerControl.RightTrigger => new AppControllerOutput(AppControllerOutputKind.MouseLeftClick),
                    AppControllerControl.LeftTrigger => new AppControllerOutput(AppControllerOutputKind.MouseRightClick),
                    _ => null
                };

                if (pointerOutput is not null)
                {
                    _appControllerRuntime.Execute(pointerOutput);
                }
            }
            return;
        }

        if (!_controllerInput.AppInputMode ||
            IsVisible ||
            !_foregroundLaunchSessionId.HasValue ||
            _foregroundControllerProfileSessionId != _foregroundLaunchSessionId ||
            _foregroundAppControllerProfile is not { Enabled: true } profile)
        {
            return;
        }

        var mapping = profile.Mappings.FirstOrDefault(candidate => candidate.Control == input.Control);
        if (mapping is null || mapping.Output.Kind == AppControllerOutputKind.None)
        {
            return;
        }

        var launchSessionId = _foregroundLaunchSessionId.Value;
        try
        {
            _appControllerRuntime.Execute(
                mapping.Output,
                focusManagedApp: () => TryActivateManagedAppWindow(launchSessionId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            _installedLibraryView.ShowStatus($"Controller action failed: {ex.Message}");
        }
    }

    private void HandleForegroundAppAnalog(ControllerAnalogEventArgs input)
    {
        if (_overlayWindow.IsOpen)
        {
            if (_controllerInput.AppInputMode)
            {
                // Right stick remains a pointer over Grev's overlay. Left stick still scrolls the
                // overlay while its directional events also provide controller focus navigation.
                _appControllerRuntime.ExecuteAnalog(
                    new AppControllerOutput(AppControllerOutputKind.MouseCursor),
                    input.RightX,
                    input.RightY);
                _appControllerRuntime.ExecuteAnalog(
                    new AppControllerOutput(AppControllerOutputKind.MouseScroll),
                    input.LeftX,
                    input.LeftY);
            }
            return;
        }

        if (!_controllerInput.AppInputMode ||
            IsVisible ||
            _foregroundControllerProfileSessionId != _foregroundLaunchSessionId ||
            _foregroundAppControllerProfile is not { Enabled: true } profile)
        {
            return;
        }

        var left = profile.Mappings.FirstOrDefault(candidate => candidate.Control == AppControllerControl.LeftStick);
        if (left is not null)
        {
            _appControllerRuntime.ExecuteAnalog(left.Output, input.LeftX, input.LeftY);
        }

        var right = profile.Mappings.FirstOrDefault(candidate => candidate.Control == AppControllerControl.RightStick);
        if (right is not null)
        {
            _appControllerRuntime.ExecuteAnalog(right.Output, input.RightX, input.RightY);
        }
    }

    private void ClearForegroundAppControllerProfile()
    {
        _foregroundControllerProfileSessionId = null;
        _foregroundAppControllerProfile = null;
        if (IsVisible || !_foregroundLaunchSessionId.HasValue)
        {
            _controllerInput.AppInputMode = false;
        }
    }
}
