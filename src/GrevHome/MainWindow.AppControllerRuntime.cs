using System.Windows;
using GrevHome.Input;
using GrevHome.Runtime;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private static readonly TimeSpan ForegroundWindowPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ForegroundWindowHiddenGrace = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ManagedCloseEscalationDelay = TimeSpan.FromSeconds(3);

    private readonly AppControllerRuntimeService _appControllerRuntime = new();
    private readonly ProcessWindowService _appProcessWindows = new();
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

        // A normal Close request should end the managed session even when an app interprets
        // WM_CLOSE as "hide to tray" (Discord does this). Give the app a short graceful window,
        // then terminate only the same Grev-tracked process identities if they are still alive.
        _runningAppsView.CloseRequested += ScheduleManagedCloseEscalation;
        _appKillerView.CloseRequested += ScheduleManagedCloseEscalation;
        _installedLibraryView.CloseRequested += ScheduleManagedCloseEscalation;
        _overlayWindow.CloseRequested += ScheduleManagedCloseEscalation;
        _runtimeSessions.SessionChanged += snapshot =>
        {
            if (snapshot.State == LaunchSessionState.Closing)
            {
                ScheduleManagedCloseEscalation(snapshot.LaunchSessionId);
            }
        };

        _overlayWindow.ControllerGuideDontShowAgainRequested += (grevId, appId) =>
        {
            try
            {
                _appControllerGuidePreferences.DisableForProfile(grevId, appId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _installedLibraryView.ShowStatus($"Could not save controller-guide preference: {ex.Message}");
            }
        };

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
        StartForegroundWindowWatch(launchSessionId);
        _ = EnsureForegroundAppActivatedAsync(launchSessionId);
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
        return _appProcessWindows.TryActivate(
            snapshot.ProcessIds,
            maximize: package?.LaunchMaximized == true);
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
        if (package is not { ShowControllerGuideOnLaunch: true } ||
            package.ControllerGuideControls is not { Count: > 0 })
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

        var controls = package.ControllerGuideControls
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
            snapshot.AppName,
            snapshot.PrimaryGrevId,
            FormatSystemShortcut(ControllerShortcutAction.ReturnHome),
            FormatSystemShortcut(ControllerShortcutAction.Overlay),
            controls);
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

            // If the session still exists, its normal close either failed or only hid the app to
            // its tray. ForceClose is still identity-validated and is scoped to this Grev session.
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

                var state = _appProcessWindows.GetWindowState(snapshot.ProcessIds);
                if (state == RuntimeWindowState.Visible)
                {
                    observedVisibleWindow = true;
                    hiddenSince = null;
                }
                else if (observedVisibleWindow)
                {
                    hiddenSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - hiddenSince.Value >= ForegroundWindowHiddenGrace)
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

                await Task.Delay(ForegroundWindowPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when Grev Home becomes visible again or control moves to another app.
        }
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
        if (_overlayWindow.IsOpen ||
            !_controllerInput.AppInputMode ||
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
        if (_overlayWindow.IsOpen ||
            !_controllerInput.AppInputMode ||
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
