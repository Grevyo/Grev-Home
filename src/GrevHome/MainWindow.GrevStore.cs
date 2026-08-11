using System.ComponentModel;
using System.IO;
using System.Net.Http;
using GrevHome.Apps;
using GrevHome.Navigation;
using GrevHome.Store;
using GrevHome.Store.Installers;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly GrevStoreView _grevStoreView = new();
    private readonly GrevStoreAppView _grevStoreAppView = new();
    private readonly GrevStoreCatalogService _grevStoreCatalog = new();
    private GrevStorePackageDefinition? _selectedStorePackage;
    private RetroArchInstallerService? _retroArchInstaller;
    private bool _grevStoreIntegrationReady;
    private bool _storeInstallBusy;

    private void InitializeGrevStoreIntegration()
    {
        if (_grevStoreIntegrationReady) return;
        _grevStoreIntegrationReady = true;

        _retroArchInstaller = new RetroArchInstallerService(_paths, _installedApps);
        _dashboardView.StoreRequested += (_, _) => OpenGrevStore();
        _grevStoreView.PackageRequested += OpenStorePackage;
        _grevStoreAppView.DownloadRequested += package => _ = BeginStoreDownloadAsync(package);
        _grevStoreAppView.OpenRequested += entry => _ = LaunchInstalledAppAsync(entry);
        _grevStoreAppView.UninstallRequested += BeginStoreUninstall;
        _navigation.RouteChanged += HandleGrevStoreRouteChanged;
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.GrevStore)
            {
                Dispatcher.BeginInvoke(new Action(RefreshGrevStore));
            }
            else if (_navigation.Current == Route.GrevStoreApp && !_storeInstallBusy)
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
                if (!_storeInstallBusy)
                {
                    _ = RefreshSelectedStorePackageAsync();
                }
                FocusRouteSoon();
                break;
        }
    }

    private void OpenStorePackage(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy) return;
        _selectedStorePackage = package;
        _navigation.Navigate(Route.GrevStoreApp);
    }

    private async Task RefreshSelectedStorePackageAsync()
    {
        var package = _selectedStorePackage;
        if (package is null || _navigation.Current != Route.GrevStoreApp || _storeInstallBusy) return;

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

        if (_navigation.Current != Route.GrevStoreApp || _selectedStorePackage != package || _storeInstallBusy) return;

        var installLocation = package.IsProfileInstall
            ? string.IsNullOrWhiteSpace(grevId)
                ? $"Profiles\\<GrevID>\\Apps\\{package.App.AppId}"
                : _paths.GetProfileAppRoot(grevId, package.App.AppId)
            : _paths.GetGlobalAppRoot(package.App.AppId);

        _grevStoreAppView.SetPackage(package, primary, installedEntry, installLocation);
    }

    private async Task BeginStoreDownloadAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy) return;

        var grevId = _session.PrimaryUser?.GrevId;
        if (package.IsProfileInstall && string.IsNullOrWhiteSpace(grevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to download this Profile App.");
            return;
        }

        if (!string.Equals(package.InstallerId, RetroArchInstallerService.InstallerId, StringComparison.OrdinalIgnoreCase) ||
            _retroArchInstaller is null)
        {
            _grevStoreAppView.ShowStatus($"Trusted installer '{package.InstallerId}' is not implemented yet.");
            return;
        }

        _storeInstallBusy = true;
        try
        {
            var progress = new Progress<PackageInstallProgress>(update =>
            {
                if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
                {
                    _grevStoreAppView.SetBusy(update.Stage, update.Message, update.Percent);
                }
            });

            await _retroArchInstaller.InstallAsync(package, grevId!, progress);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or
                                   InvalidOperationException or InvalidDataException or Win32Exception)
        {
            _grevStoreAppView.ShowStatus($"Install failed: {ex.Message}");
        }
        finally
        {
            _storeInstallBusy = false;
        }

        if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
        {
            await RefreshSelectedStorePackageAsync();
        }
    }

    private void BeginStoreUninstall(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy) return;
        _grevStoreAppView.ShowStatus(
            $"Uninstall is reserved for trusted installer '{package.InstallerId}'. " +
            "The package-specific uninstall workflow is the next installer action; Grev Home will not perform an unsafe generic folder deletion.");
    }
}
