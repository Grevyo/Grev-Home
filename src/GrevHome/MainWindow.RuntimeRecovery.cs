using System.ComponentModel;
using System.IO;
using GrevHome.Apps;

namespace GrevHome;

public partial class MainWindow
{
    private bool _runtimeRecoveryIntegrationReady;

    private void InitializeRuntimeRecoveryIntegration()
    {
        if (_runtimeRecoveryIntegrationReady)
        {
            return;
        }

        _runtimeRecoveryIntegrationReady = true;
        _runningAppsView.RestartRequested += launchSessionId => _ = RestartRuntimeSessionAsync(launchSessionId);
        _overlayWindow.RestartRequested += launchSessionId => _ = RestartRuntimeSessionAsync(launchSessionId);
    }

    private async Task RestartRuntimeSessionAsync(Guid launchSessionId)
    {
        var snapshot = _runtimeSessions.GetSession(launchSessionId);
        if (snapshot is null)
        {
            _runningAppsView.ShowStatus("That app is no longer running.");
            UpdateRuntimeSurfaces();
            return;
        }

        try
        {
            _runningAppsView.ShowStatus($"Restarting {snapshot.AppName}…");

            var installed = await _installedApps.GetInstalledForUserAsync(snapshot.PrimaryGrevId);
            var entry = installed.FirstOrDefault(candidate =>
                candidate.AvailableToCurrentUser &&
                string.Equals(
                    candidate.Manifest.Definition.AppId,
                    snapshot.AppId,
                    StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                throw new InvalidOperationException(
                    $"{snapshot.AppName} is no longer registered as an installed app for the launch profile, so Grev Home will not guess how to restart it.");
            }

            _overlayWindow.Dismiss();
            var restarted = await _runtimeSessions.RestartAsync(launchSessionId, entry);
            _foregroundLaunchSessionId = restarted.LaunchSessionId;
            UpdateRuntimeSurfaces();
            Hide();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            UpdateRuntimeSurfaces();
            _runningAppsView.ShowStatus($"Restart failed: {ex.Message}");

            if (_session.HasSignedInUsers)
            {
                OpenRunningApps();
            }

            RestoreWindowWithoutChangingRoute();
        }
    }
}
