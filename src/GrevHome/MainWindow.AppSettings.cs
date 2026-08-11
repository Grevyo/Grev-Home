using System.IO;
using System.Windows;
using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly AppSettingsView _appSettingsView = new();
    private AppControllerProfileService? _appControllerProfileService;
    private InstalledAppEntry? _appSettingsEntry;
    private bool _appSettingsIntegrationReady;

    private void InitializeAppSettingsIntegration()
    {
        if (_appSettingsIntegrationReady) return;
        _appSettingsIntegrationReady = true;

        _appControllerProfileService = new AppControllerProfileService(_paths);
        _installedLibraryView.SettingsRequested += OpenAppSettings;
        _grevStoreAppView.SettingsRequested += OpenAppSettings;
        _appSettingsView.SaveRequested += draft => _ = SaveAppControllerProfileAsync(draft);
        _appSettingsView.ResetRequested += (_, _) => _ = ResetAppControllerProfileAsync();
        _appSettingsView.BackRequested += (_, _) => _navigation.GoBack();
        _navigation.RouteChanged += HandleAppSettingsRouteChanged;
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.AppSettings)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshAppSettingsAsync()));
            }
        };
    }

    private void OpenAppSettings(InstalledAppEntry entry)
    {
        if (!_session.HasSignedInUsers || !entry.AvailableToCurrentUser)
        {
            return;
        }

        _appSettingsEntry = entry;
        _navigation.Navigate(Route.AppSettings);
    }

    private void HandleAppSettingsRouteChanged(Route route)
    {
        if (route != Route.AppSettings) return;
        RouteHost.Content = _appSettingsView;
        _ = RefreshAppSettingsAsync();
        FocusRouteSoon();
    }

    private async Task RefreshAppSettingsAsync()
    {
        var entry = _appSettingsEntry;
        var service = _appControllerProfileService;
        if (entry is null || service is null || _navigation.Current != Route.AppSettings) return;

        var primary = _session.PrimaryUser;
        var grevId = primary?.GrevId;
        var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
        var defaults = package?.ControllerProfile ?? AppControllerProfileDefaults.Empty;

        ResolvedAppControllerProfile resolved;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            resolved = AppControllerProfileService.ResolveDefaults(defaults);
        }
        else
        {
            resolved = await service.ResolveAsync(grevId, entry.Manifest.Definition.AppId, defaults);
        }

        if (_navigation.Current != Route.AppSettings || _appSettingsEntry != entry) return;

        _appSettingsView.SetApp(
            entry,
            package,
            primary,
            resolved,
            canSave: !string.IsNullOrWhiteSpace(grevId));
    }

    private async Task SaveAppControllerProfileAsync(AppControllerProfileDraft draft)
    {
        var entry = _appSettingsEntry;
        var service = _appControllerProfileService;
        var grevId = _session.PrimaryUser?.GrevId;
        if (entry is null || service is null || string.IsNullOrWhiteSpace(grevId))
        {
            _appSettingsView.ShowStatus("A persistent local Primary GrevID is required to save app-specific settings.");
            return;
        }

        try
        {
            await service.SaveAsync(grevId, entry.Manifest.Definition.AppId, draft.Enabled, draft.Mappings);
            await RefreshAppSettingsAsync();
            _appSettingsView.ShowStatus("Controller profile saved for this GrevID. Other profiles keep their own app settings.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _appSettingsView.ShowStatus($"Could not save controller profile: {ex.Message}");
        }
    }

    private async Task ResetAppControllerProfileAsync()
    {
        var entry = _appSettingsEntry;
        var service = _appControllerProfileService;
        var grevId = _session.PrimaryUser?.GrevId;
        if (entry is null || service is null || string.IsNullOrWhiteSpace(grevId))
        {
            _appSettingsView.ShowStatus("A persistent local Primary GrevID is required to reset app-specific settings.");
            return;
        }

        try
        {
            await service.ResetAsync(grevId, entry.Manifest.Definition.AppId);
            await RefreshAppSettingsAsync();
            _appSettingsView.ShowStatus("Controller profile reset. Grev Home is showing this app's supplied defaults again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _appSettingsView.ShowStatus($"Could not reset controller profile: {ex.Message}");
        }
    }
}
