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
    private readonly System.Windows.Threading.DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    private void InitializeUpdates()
    {
        _updateService = new GrevHomeUpdateService(_paths);
        _activityCenterView.InstallUpdateRequested += (_, _) => _ = InstallAvailableUpdateAsync();
        _activityCenterView.CheckUpdatesRequested += (_, _) => _ = CheckForUpdatesAsync(manual: true);
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _updateTimer.Start();
        Closed += (_, _) => _updateTimer.Stop();
        _session.Changed += (_, _) =>
        {
            if (!_updateCheckStarted && _session.PrimaryUser?.GrevId is not null)
            {
                _updateCheckStarted = true;
                _ = CheckForUpdatesAsync();
            }
        };
    }

    private async Task CheckForUpdatesAsync(bool manual = false)
    {
        try
        {
            _availableUpdate = await (_updateService?.CheckAsync() ?? Task.FromResult<GrevHomeUpdate?>(null));
            if (_availableUpdate is null || _notificationService is null)
            {
                if (manual) _activityCenterView.ShowStatus("No newer complete release is available.");
                return;
            }
            if (manual) _activityCenterView.ShowStatus($"Version {_availableUpdate.Version} is available. Choose Install update in notifications.");
            var grevId = _session.PrimaryUser?.GrevId;
            var existing = grevId is null ? NotificationSnapshot.Empty : await _notificationService.GetForGrevIdAsync(grevId, 100);
            var title = $"Grev Home {_availableUpdate.Version} is ready";
            if (existing.Items.Any(item => item.Source == "Grev Home Update" && item.Title == title)) return;
            await _notificationService.PublishAsync(NotificationSeverity.Info, "Grev Home Update", title,
                "Open Activity Center and choose Install update. Profiles, games, artwork and settings are kept.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or JsonException)
        {
            if (manual) _activityCenterView.ShowStatus("Could not check for updates. Check your connection and try again.");
        }
    }

    private async Task InstallAvailableUpdateAsync()
    {
        if (_updateInstalling || _updateService is null) return;
        if (_runtimeSessions.GetActiveSessions().Count > 0)
        {
            _activityCenterView.ShowStatus("Close running games and apps before updating Grev Home.");
            return;
        }
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
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or System.ComponentModel.Win32Exception or JsonException)
        {
            _activityCenterView.ShowStatus($"The update could not be installed: {ex.Message}");
        }
        finally { _updateInstalling = false; }
    }
}
