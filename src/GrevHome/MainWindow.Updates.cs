using System.IO;
using System.Text.Json;
using System.Windows;
using GrevHome.Notifications;
using GrevHome.Updates;

namespace GrevHome;

public partial class MainWindow
{
    private GrevHomeUpdateService? _updateService;
    private GrevHomeUpdate? _availableUpdate;
    private bool _updateInstalling;
    private bool _updateCheckStarted;

    private void InitializeUpdates()
    {
        _updateService = new GrevHomeUpdateService(_paths);
        _activityCenterView.InstallUpdateRequested += (_, _) => _ = InstallAvailableUpdateAsync();
        _session.Changed += (_, _) =>
        {
            if (!_updateCheckStarted && _session.PrimaryUser?.GrevId is not null)
            {
                _updateCheckStarted = true;
                _ = CheckForUpdatesAsync();
            }
        };
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _availableUpdate = await (_updateService?.CheckAsync() ?? Task.FromResult<GrevHomeUpdate?>(null));
            if (_availableUpdate is null || _notificationService is null) return;
            var grevId = _session.PrimaryUser?.GrevId;
            var existing = grevId is null ? NotificationSnapshot.Empty : await _notificationService.GetForGrevIdAsync(grevId, 100);
            var title = $"Grev Home {_availableUpdate.Version} is ready";
            if (existing.Items.Any(item => item.Source == "Grev Home Update" && item.Title == title)) return;
            await _notificationService.PublishAsync(NotificationSeverity.Info, "Grev Home Update", title,
                "Open Activity Center and choose Install update. Profiles, games, artwork and settings are kept.", grevId);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or JsonException)
        {
            // Update checks never interrupt startup. The next launch checks again.
        }
    }

    private async Task InstallAvailableUpdateAsync()
    {
        if (_updateInstalling || _updateService is null) return;
        _updateInstalling = true;
        _activityCenterView.ShowStatus("Downloading and checking the Grev Home update…");
        try
        {
            _availableUpdate ??= await _updateService.CheckAsync();
            if (_availableUpdate is null) { _activityCenterView.ShowStatus("Grev Home is already up to date."); return; }
            await _updateService.DownloadAndLaunchAsync(_availableUpdate);
            _activityCenterView.ShowStatus("The verified installer is starting. Grev Home will close when Setup is ready.");
            await Task.Delay(600);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            _activityCenterView.ShowStatus($"The update could not be installed: {ex.Message}");
        }
        finally { _updateInstalling = false; }
    }
}
