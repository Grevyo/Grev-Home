using System.ComponentModel;
using System.IO;
using GrevHome.Apps;
using GrevHome.Games;
using GrevHome.Navigation;
using GrevHome.Notifications;

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
        _appKillerView.RestartRequested += launchSessionId => _ = RestartRuntimeSessionAsync(launchSessionId);
        _overlayWindow.RestartRequested += launchSessionId => _ = RestartRuntimeSessionAsync(launchSessionId);
    }

    private async Task RestartRuntimeSessionAsync(Guid launchSessionId)
    {
        var requestedFromAppKiller = _navigation.Current == Route.AppKiller;
        var snapshot = _runtimeSessions.GetSession(launchSessionId);
        if (snapshot is null)
        {
            if (requestedFromAppKiller)
            {
                _appKillerView.ShowStatus("That app is no longer running.");
            }
            else
            {
                _runningAppsView.ShowStatus("That app is no longer running.");
            }

            UpdateRuntimeSurfaces();
            return;
        }

        try
        {
            if (requestedFromAppKiller)
            {
                _appKillerView.ShowStatus($"Restarting {snapshot.AppName}…");
            }
            else
            {
                _runningAppsView.ShowStatus($"Restarting {snapshot.AppName}…");
            }

            var installed = await _installedApps.GetInstalledForUserAsync(snapshot.PrimaryGrevId);
            var entry = installed.FirstOrDefault(candidate =>
                candidate.AvailableToCurrentUser &&
                string.Equals(
                    candidate.Manifest.Definition.AppId,
                    snapshot.AppId,
                    StringComparison.OrdinalIgnoreCase));

            // Individual games intentionally are not fake InstalledApp manifests. Reconstruct the
            // exact runtime entry from the owning GrevID's game library and platform resolver so
            // Restart keeps the normal crash-safe close/finalize/relaunch flow while still using
            // that same profile's emulator binaries, BIOS/config and save data.
            if (entry is null &&
                snapshot.AppId.StartsWith("game.", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(snapshot.PrimaryGrevId) &&
                _gameLibraryService is not null)
            {
                var games = await _gameLibraryService.GetForProfileAsync(snapshot.PrimaryGrevId);
                var game = games.FirstOrDefault(candidate =>
                    string.Equals(candidate.GameId, snapshot.AppId, StringComparison.OrdinalIgnoreCase));
                if (game is not null)
                {
                    entry = _gameLaunchResolver.Resolve(game, installed, snapshot.PrimaryGrevId);
                }
            }

            if (entry is null)
            {
                throw new InvalidOperationException(
                    $"{snapshot.AppName} is no longer registered in the launch profile's app or game library, so Grev Home will not guess how to restart it.");
            }

            _overlayWindow.Dismiss();
            var restarted = await _runtimeSessions.RestartAsync(launchSessionId, entry);
            _foregroundLaunchSessionId = restarted.LaunchSessionId;
            UpdateRuntimeSurfaces();
            Hide();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception)
        {
            UpdateRuntimeSurfaces();

            if (requestedFromAppKiller)
            {
                _appKillerView.ShowStatus($"Restart failed: {ex.Message}");
            }
            else
            {
                _runningAppsView.ShowStatus($"Restart failed: {ex.Message}");
                if (_session.HasSignedInUsers)
                {
                    OpenRunningApps();
                }
            }

            await TryPublishActivityNotificationAsync(
                NotificationSeverity.Error,
                "Runtime",
                $"{snapshot.AppName} restart failed",
                LimitNotificationMessage(ex.Message),
                snapshot.PrimaryGrevId);

            RestoreWindowWithoutChangingRoute();
        }
    }
}
