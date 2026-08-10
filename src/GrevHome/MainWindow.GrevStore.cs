using GrevHome.Apps;
using GrevHome.Navigation;
using GrevHome.Store;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly GrevStoreView _grevStoreView = new();
    private readonly GrevStoreAppView _grevStoreAppView = new();
    private readonly GrevStoreCatalogService _grevStoreCatalog = new();
    private GrevStorePackageDefinition? _selectedStorePackage;
    private bool _grevStoreIntegrationReady;

    private void InitializeGrevStoreIntegration()
    {
        if (_grevStoreIntegrationReady) return;
        _grevStoreIntegrationReady = true;

        _dashboardView.StoreRequested += (_, _) => OpenGrevStore();
        _grevStoreView.PackageRequested += OpenStorePackage;
        _grevStoreAppView.DownloadRequested += BeginStoreDownload;
        _grevStoreAppView.OpenRequested += entry => _ = LaunchInstalledAppAsync(entry);
        _grevStoreAppView.UninstallRequested += BeginStoreUninstall;
        _navigation.RouteChanged += HandleGrevStoreRouteChanged;
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.GrevStore)
            {
                Dispatcher.BeginInvoke(new Action(RefreshGrevStore));
            }
            else if (_navigation.Current == Route.GrevStoreApp)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshSelectedStorePackageAsync()));
            }
        };
    }

    private void OpenGrevStore()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        RefreshGrevStore();
        _navigation.Navigate(Route.GrevStore);
    }

    private void RefreshGrevStore() =>
        _grevStoreView.SetStore(_grevStoreCatalog.GetAll(), _session.PrimaryUser);

    private void HandleGrevStoreRouteChanged(Route route)
    {
        switch (route)
        {
            case Route.GrevStore:
                RefreshGrevStore();
                RouteHost.Content = _grevStoreView;
                FocusRouteSoon();
                break;
            case Route.GrevStoreApp:
                RouteHost.Content = _grevStoreAppView;
                _ = RefreshSelectedStorePackageAsync();
                FocusRouteSoon();
                break;
        }
    }

    private void OpenStorePackage(GrevStorePackageDefinition package)
    {
        _selectedStorePackage = package;
        _navigation.Navigate(Route.GrevStoreApp);
    }

    private async Task RefreshSelectedStorePackageAsync()
    {
        var package = _selectedStorePackage;
        if (package is null || _navigation.Current != Route.GrevStoreApp) return;

        var primary = _session.PrimaryUser;
        var grevId = primary?.GrevId;
        InstalledAppEntry? installedEntry = null;

        try
        {
            var entries = await _installedApps.GetInstalledForUserAsync(grevId);
            installedEntry = entries.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
                (package.IsProfileInstall
                    ? !string.IsNullOrWhiteSpace(grevId) && string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase)
                    : entry.Manifest.OwnerGrevId is null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _grevStoreAppView.ShowStatus($"Installed-state check failed: {ex.Message}");
        }

        if (_navigation.Current != Route.GrevStoreApp || _selectedStorePackage != package) return;

        var installLocation = package.IsProfileInstall
            ? string.IsNullOrWhiteSpace(grevId)
                ? $"Profiles\\<GrevID>\\Apps\\{package.App.AppId}"
                : _paths.GetProfileAppRoot(grevId, package.App.AppId)
            : _paths.GetGlobalAppRoot(package.App.AppId);

        _grevStoreAppView.SetPackage(package, primary, installedEntry, installLocation);
    }

    private void BeginStoreDownload(GrevStorePackageDefinition package)
    {
        if (package.IsProfileInstall && string.IsNullOrWhiteSpace(_session.PrimaryUser?.GrevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to download this Profile App.");
            return;
        }

        _grevStoreAppView.ShowStatus(
            $"Download is ready to hand off to trusted installer '{package.InstallerId}'. " +
            "The RetroArch package-specific downloader/install workflow is the next 0.11 step.");
    }

    private void BeginStoreUninstall(GrevStorePackageDefinition package)
    {
        _grevStoreAppView.ShowStatus(
            $"Uninstall is reserved for trusted installer '{package.InstallerId}'. " +
            "The package-specific uninstall workflow will be added with the RetroArch installer so Grev Home never performs an unsafe generic folder deletion.");
    }
}
