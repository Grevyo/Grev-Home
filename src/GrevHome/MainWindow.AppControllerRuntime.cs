using System.Windows;
using GrevHome.Input;
using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private static readonly TimeSpan ForegroundWindowPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ForegroundWindowHiddenGrace = TimeSpan.FromMilliseconds(650);

    private readonly AppControllerRuntimeService _appControllerRuntime = new();
    private readonly ProcessWindowService _appProcessWindows = new();
    private ResolvedAppControllerProfile? _foregroundAppControllerProfile;
    private Guid? _foregroundControllerProfileSessionId;
    private CancellationTokenSource? _foregroundWindowWatchCts;
    private bool _appControllerRuntimeIntegrationReady;

    private void InitializeAppControllerRuntimeIntegration()
    {
        if (_appControllerRuntimeIntegrationReady) return;
        _appControllerRuntimeIntegrationReady = true;

        _controllerInput.AppControlPressed += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleForegroundAppControl(input)));
        _controllerInput.AnalogChanged += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleForegroundAppAnalog(input)));
        IsVisibleChanged += (_, _) => HandleShellVisibilityChanged();
        _runtimeSessions.SessionEnded += snapshot =>
        {
            if (_foregroundControllerProfileSessionId == snapshot.LaunchSessionId)
            {
                Dispatcher.BeginInvoke(new Action(ClearForegroundAppControllerProfile));
            }
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
