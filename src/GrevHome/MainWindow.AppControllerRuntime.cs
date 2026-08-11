using System.Windows;
using GrevHome.Input;

namespace GrevHome;

public partial class MainWindow
{
    private readonly AppControllerRuntimeService _appControllerRuntime = new();
    private ResolvedAppControllerProfile? _foregroundAppControllerProfile;
    private Guid? _foregroundControllerProfileSessionId;
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

        if (!IsVisible && _foregroundLaunchSessionId.HasValue)
        {
            _ = EnsureForegroundAppActivatedAsync(_foregroundLaunchSessionId.Value);
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

            if (_runtimeSessions.SwitchTo(launchSessionId))
            {
                return;
            }

            await Task.Delay(125);
        }

        if (IsVisible || _foregroundLaunchSessionId != launchSessionId)
        {
            return;
        }

        RestoreWindowWithoutChangingRoute();
        const string message = "The app process started, but Grev Home could not find a usable app window to bring forward. The app remains tracked in Running Apps/App Killer if its process is still active.";
        _installedLibraryView.ShowLaunchError(message);
        _grevStoreAppView.ShowStatus(message);
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
                focusManagedApp: () => _runtimeSessions.SwitchTo(launchSessionId));
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
