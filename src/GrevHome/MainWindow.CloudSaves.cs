using GrevHome.Online;

namespace GrevHome;

public partial class MainWindow
{
    /// <summary>
    /// Steam-like launch gate: pull a one-sided newer cloud copy before starting, block an
    /// unresolved two-sided conflict, and allow offline/local-only play without making the cloud
    /// service a launch dependency.
    /// </summary>
    private async Task PrepareCloudSaveForLaunchAsync(string grevId, string appId, string appName)
    {
        var sync = _grevDad.SaveSyncService;
        if (sync is null || !await sync.IsEnabledAsync(grevId, appId)) return;

        var state = await sync.CheckRemoteAsync(grevId, appId);
        if (state.Status == CloudSaveStatus.Conflict)
            throw new InvalidOperationException(
                $"{appName} has a cloud-save conflict. Open its App Settings > Cloud Saves and choose Keep This Device or Use Cloud before playing.");

        if (state.Status == CloudSaveStatus.RemoteChangesAvailable)
        {
            var restored = await sync.DownloadAsync(grevId, appId);
            if (restored.Status != CloudSaveStatus.UpToDate)
                throw new InvalidOperationException(restored.Message ??
                    $"{appName}'s newer cloud save could not be downloaded. Launch was stopped so the two copies do not diverge.");
        }

        if (state.Status == CloudSaveStatus.Error)
            throw new InvalidOperationException(state.Message ??
                $"{appName}'s save could not be checked completely. Launch was stopped to protect it.");

        // Offline and NotLinked deliberately continue. Local play remains possible; the local
        // content hash stays pending and the post-session uploader will warn if delivery fails.
    }
}
