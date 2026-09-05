using System.IO;
using GrevHome.Input;
using GrevHome.Store;

namespace GrevHome;

public partial class MainWindow
{
    /// <summary>
    /// Handles an onboarding shortcut which disables the current app's Grev controller profile
    /// for one persistent GrevID. The mappings are preserved so App Settings can re-enable the
    /// same setup layer later without reconstructing it or changing the app's native controller
    /// configuration.
    /// </summary>
    private async Task DisableAppControllerProfileFromGuideAsync(string grevId, string appId)
    {
        if (string.IsNullOrWhiteSpace(grevId) || string.IsNullOrWhiteSpace(appId))
        {
            return;
        }

        var package = _grevStoreCatalog.Find(appId);
        if (package is null ||
            !package.Supports(AppPackageCapability.ControllerProfile) ||
            package.ControllerProfile is null)
        {
            _installedLibraryView.ShowStatus("This app does not expose a Grev controller profile to disable.");
            return;
        }

        var service = _appControllerProfileService ??= new AppControllerProfileService(_paths);
        try
        {
            var resolved = await service.ResolveAsync(grevId, appId, package.ControllerProfile);
            await service.SaveAsync(grevId, appId, enabled: false, resolved.Mappings);

            if (_foregroundLaunchSessionId is Guid launchSessionId)
            {
                var foreground = _runtimeSessions.GetSession(launchSessionId) ??
                                 _runtimeSessions.GetActiveSessions()
                                     .FirstOrDefault(session => session.LaunchSessionId == launchSessionId);
                if (foreground is not null &&
                    string.Equals(foreground.AppId, appId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(foreground.PrimaryGrevId, grevId, StringComparison.OrdinalIgnoreCase))
                {
                    _foregroundControllerProfileSessionId = launchSessionId;
                    _foregroundAppControllerProfile = resolved with
                    {
                        Enabled = false,
                        HasUserOverride = true
                    };
                }
            }

            var displayName = string.IsNullOrWhiteSpace(package.Onboarding?.ControllerProfileDisplayName)
                ? "Grev controller profile"
                : package.Onboarding.ControllerProfileDisplayName;
            _installedLibraryView.ShowStatus(
                $"{displayName} disabled for this GrevID. The app's native controller support was not changed. Re-enable it from App Settings whenever setup controls are needed again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _installedLibraryView.ShowStatus($"Could not disable the app setup controls: {ex.Message}");
        }
    }
}
