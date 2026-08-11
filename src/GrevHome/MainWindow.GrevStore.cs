using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
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
    private GrevStorePackageDefinition? _storeUninstallWarningPackage;
    private RetroArchInstallerService? _retroArchInstaller;
    private DiscordInstallerService? _discordInstaller;
    private StoreRouteTransition _storeRouteTransition;
    private bool _grevStoreIntegrationReady;
    private bool _storeInstallBusy;

    private bool IsStoreModalOpen => StoreModalOverlay.Visibility == Visibility.Visible;

    private void InitializeGrevStoreIntegration()
    {
        if (_grevStoreIntegrationReady) return;
        _grevStoreIntegrationReady = true;
        InitializeAppControllerRuntimeIntegration();

        _retroArchInstaller = new RetroArchInstallerService(_paths, _installedApps);
        _discordInstaller = new DiscordInstallerService(_paths, _installedApps);
        _dashboardView.StoreRequested += (_, _) => OpenGrevStore();
        _grevStoreView.PackageRequested += OpenStorePackage;
        _grevStoreAppView.DownloadRequested += package => _ = BeginStoreDownloadAsync(package);
        _grevStoreAppView.OpenRequested += entry => _ = LaunchInstalledAppAsync(entry);
        _grevStoreAppView.UninstallRequested += package =>
        {
            if (package.IsProfileInstall)
            {
                ShowStoreUninstallWarning(package);
            }
            else
            {
                _ = RemoveGlobalAppFromLibraryAsync(package);
            }
        };
        _navigation.RouteChanged += HandleGrevStoreRouteChanged;
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.GrevStore)
            {
                Dispatcher.BeginInvoke(new Action(RefreshGrevStore));
            }
            else if (_navigation.Current == Route.GrevStoreApp && !_storeInstallBusy && _storeUninstallWarningPackage is null)
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

                if (_storeRouteTransition == StoreRouteTransition.WarningPush)
                {
                    _storeRouteTransition = StoreRouteTransition.None;
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(() => StoreWarningCancelButton.Focus()));
                    break;
                }

                if (_storeUninstallWarningPackage is not null)
                {
                    HideStoreUninstallWarning(discardBackEntry: false, showCancelledStatus: true);
                    _storeRouteTransition = StoreRouteTransition.None;
                    FocusRouteSoon();
                    break;
                }

                _storeRouteTransition = StoreRouteTransition.None;
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
        if (_storeInstallBusy || IsStoreModalOpen) return;
        _selectedStorePackage = package;
        _navigation.Navigate(Route.GrevStoreApp);
    }

    private async Task RefreshSelectedStorePackageAsync()
    {
        var package = _selectedStorePackage;
        if (package is null || _navigation.Current != Route.GrevStoreApp || _storeInstallBusy || _storeUninstallWarningPackage is not null) return;

        var primary = _session.PrimaryUser;
        var grevId = primary?.GrevId;
        InstalledAppEntry? installedEntry = null;
        var isInCurrentUserLibrary = true;

        try
        {
            if (package.IsProfileInstall)
            {
                var entries = await _installedApps.GetInstalledForUserAsync(grevId);
                installedEntry = entries.FirstOrDefault(entry =>
                    string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(grevId) &&
                    string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var machineEntries = await _installedApps.GetMachineInstalledAsync();
                installedEntry = machineEntries.FirstOrDefault(entry =>
                    string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase));
                isInCurrentUserLibrary = await _installedApps.IsGlobalAppInUserLibraryAsync(grevId, package.App.AppId);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _grevStoreAppView.ShowStatus($"Installed-state check failed: {ex.Message}");
        }

        if (_navigation.Current != Route.GrevStoreApp || _selectedStorePackage != package || _storeInstallBusy || _storeUninstallWarningPackage is not null) return;

        var installLocation = package.App.InstallStrategy switch
        {
            InstallStrategy.GrevIdPortable when string.IsNullOrWhiteSpace(grevId) =>
                $"Profiles\\<GrevID>\\Apps\\{package.App.AppId}",
            InstallStrategy.GrevIdPortable => _paths.GetProfileAppRoot(grevId!, package.App.AppId),
            InstallStrategy.SystemInstalled =>
                Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(package.App.Launch.Executable))
                ?? Environment.ExpandEnvironmentVariables(package.App.Launch.Executable),
            _ => _paths.GetGlobalAppRoot(package.App.AppId)
        };

        _grevStoreAppView.SetPackage(
            package,
            primary,
            installedEntry,
            installLocation,
            isInCurrentUserLibrary);
    }

    private async Task BeginStoreDownloadAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen) return;

        var grevId = _session.PrimaryUser?.GrevId;
        if (package.IsProfileInstall && string.IsNullOrWhiteSpace(grevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to download this Profile App.");
            return;
        }

        if (!package.IsProfileInstall)
        {
            try
            {
                var existing = (await _installedApps.GetMachineInstalledAsync()).FirstOrDefault(entry =>
                    string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    if (string.IsNullOrWhiteSpace(grevId))
                    {
                        _grevStoreAppView.ShowStatus("This Global App is already installed on the machine. Choose a persistent local Primary User to save library membership.");
                        return;
                    }

                    await _installedApps.RestoreGlobalAppToUserLibraryAsync(grevId, package.App.AppId);
                    await RefreshSelectedStorePackageAsync();
                    _grevStoreAppView.ShowStatus($"{package.Presentation.DisplayName} was added back to this GrevID's library. No download was needed.");
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _grevStoreAppView.ShowStatus($"Library update failed: {ex.Message}");
                return;
            }
        }

        _storeInstallBusy = true;
        ShowStoreOperation(package, "Installing", "Preparing trusted installer…");
        var installedSuccessfully = false;
        try
        {
            var progress = CreateStoreProgress(package);
            if (string.Equals(package.InstallerId, RetroArchInstallerService.InstallerId, StringComparison.OrdinalIgnoreCase) &&
                _retroArchInstaller is not null)
            {
                await _retroArchInstaller.InstallAsync(package, grevId!, progress);
            }
            else if (string.Equals(package.InstallerId, DiscordInstallerService.InstallerId, StringComparison.OrdinalIgnoreCase) &&
                     _discordInstaller is not null)
            {
                await _discordInstaller.InstallAsync(package, progress);
            }
            else
            {
                throw new InvalidOperationException($"Trusted installer '{package.InstallerId}' is not implemented yet.");
            }

            installedSuccessfully = true;
            if (!package.IsProfileInstall && !string.IsNullOrWhiteSpace(grevId))
            {
                await _installedApps.RestoreGlobalAppToUserLibraryAsync(grevId, package.App.AppId);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or
                                   InvalidOperationException or InvalidDataException or Win32Exception)
        {
            _grevStoreAppView.ShowStatus($"Install failed: {ex.Message}");
        }
        finally
        {
            _storeInstallBusy = false;
            HideStoreOperation();
        }

        if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
        {
            await RefreshSelectedStorePackageAsync();
            if (installedSuccessfully)
            {
                _grevStoreAppView.ShowStatus($"{package.Presentation.DisplayName} is installed and available in this library.");
            }
        }
    }

    private async Task RemoveGlobalAppFromLibraryAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen || package.IsProfileInstall) return;

        var grevId = _session.PrimaryUser?.GrevId;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to remove a Global App from a personal library.");
            return;
        }

        try
        {
            await _installedApps.RemoveGlobalAppFromUserLibraryAsync(grevId, package.App.AppId);
            await RefreshSelectedStorePackageAsync();
            _grevStoreAppView.ShowStatus($"{package.Presentation.DisplayName} was removed from this GrevID's library. The machine installation was not changed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _grevStoreAppView.ShowStatus($"Remove from Library failed: {ex.Message}");
        }
    }

    private void ShowStoreUninstallWarning(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen) return;

        if (!package.IsProfileInstall)
        {
            _ = RemoveGlobalAppFromLibraryAsync(package);
            return;
        }

        var grevId = _session.PrimaryUser?.GrevId;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to uninstall this Profile App.");
            return;
        }

        if (IsStorePackageRunning(package, grevId))
        {
            _grevStoreAppView.ShowStatus($"Close {package.Presentation.DisplayName} before uninstalling it.");
            return;
        }

        _storeUninstallWarningPackage = package;
        StoreModalTitleText.Text = $"Final uninstall warning — {package.Presentation.DisplayName}";
        StoreProgressPanel.Visibility = Visibility.Collapsed;
        StoreWarningPanel.Visibility = Visibility.Visible;
        ShellInteractionHost.IsEnabled = false;
        StoreModalOverlay.Visibility = Visibility.Visible;

        _storeRouteTransition = StoreRouteTransition.WarningPush;
        _navigation.Navigate(Route.GrevStoreApp, allowSameRoute: true);
    }

    private async Task BeginStoreUninstallAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen) return;

        if (!package.IsProfileInstall)
        {
            _grevStoreAppView.ShowStatus("Global Apps can only be removed from a user's library here. Machine-wide uninstall is restricted to the Admin Console.");
            return;
        }

        var grevId = _session.PrimaryUser?.GrevId;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to uninstall this Profile App.");
            return;
        }

        if (IsStorePackageRunning(package, grevId))
        {
            _grevStoreAppView.ShowStatus($"Close {package.Presentation.DisplayName} before uninstalling it.");
            return;
        }

        _storeInstallBusy = true;
        ShowStoreOperation(package, "Uninstalling", "Preparing package-specific uninstall…");
        try
        {
            var progress = CreateStoreProgress(package);
            if (string.Equals(package.InstallerId, RetroArchInstallerService.InstallerId, StringComparison.OrdinalIgnoreCase) &&
                _retroArchInstaller is not null)
            {
                await _retroArchInstaller.UninstallAsync(package, grevId, progress);
            }
            else
            {
                throw new InvalidOperationException($"Trusted installer '{package.InstallerId}' does not support Profile App uninstall yet.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            _grevStoreAppView.ShowStatus($"Uninstall failed: {ex.Message}");
        }
        finally
        {
            _storeInstallBusy = false;
            HideStoreOperation();
        }

        if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
        {
            await RefreshSelectedStorePackageAsync();
        }
    }

    private bool IsStorePackageRunning(GrevStorePackageDefinition package, string? grevId) =>
        _runtimeSessions.GetActiveSessions().Any(session =>
            string.Equals(session.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
            (!package.IsProfileInstall || string.Equals(session.PrimaryGrevId, grevId, StringComparison.OrdinalIgnoreCase)));

    private IProgress<PackageInstallProgress> CreateStoreProgress(GrevStorePackageDefinition package) =>
        new Progress<PackageInstallProgress>(update =>
        {
            if (!_storeInstallBusy) return;

            if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
            {
                _grevStoreAppView.SetBusy(update.Stage, update.Message, update.Percent);
            }

            UpdateStoreOperation(update);
        });

    private void ShowStoreOperation(GrevStorePackageDefinition package, string action, string initialMessage)
    {
        _storeUninstallWarningPackage = null;
        StoreModalTitleText.Text = $"{action} {package.Presentation.DisplayName}";
        StoreWarningPanel.Visibility = Visibility.Collapsed;
        StoreProgressPanel.Visibility = Visibility.Visible;
        StoreProgressStageText.Text = "Preparing";
        StoreProgressMessageText.Text = initialMessage;
        StoreProgressBar.IsIndeterminate = false;
        StoreProgressBar.Value = 0;
        StoreProgressPercentText.Text = "0%";
        ShellInteractionHost.IsEnabled = false;
        StoreModalOverlay.Visibility = Visibility.Visible;
        _grevStoreAppView.SetBusy("Preparing", initialMessage, 0);
    }

    private void UpdateStoreOperation(PackageInstallProgress update)
    {
        if (!_storeInstallBusy || StoreModalOverlay.Visibility != Visibility.Visible || StoreProgressPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        StoreProgressStageText.Text = update.Stage;
        StoreProgressMessageText.Text = update.Message;
        StoreProgressBar.IsIndeterminate = update.Percent is null;

        if (update.Percent is null)
        {
            StoreProgressPercentText.Text = "Working…";
            return;
        }

        var percent = Math.Clamp(update.Percent.Value, 0, 100);
        StoreProgressBar.Value = percent;
        StoreProgressPercentText.Text = $"{percent:0}%";
    }

    private void HideStoreOperation()
    {
        StoreModalOverlay.Visibility = Visibility.Collapsed;
        StoreProgressPanel.Visibility = Visibility.Visible;
        StoreWarningPanel.Visibility = Visibility.Collapsed;
        StoreProgressBar.IsIndeterminate = false;
        ShellInteractionHost.IsEnabled = true;
    }

    private void HideStoreUninstallWarning(bool discardBackEntry, bool showCancelledStatus)
    {
        if (_storeUninstallWarningPackage is null) return;

        _storeUninstallWarningPackage = null;
        StoreModalOverlay.Visibility = Visibility.Collapsed;
        StoreWarningPanel.Visibility = Visibility.Collapsed;
        StoreProgressPanel.Visibility = Visibility.Visible;
        ShellInteractionHost.IsEnabled = true;

        if (discardBackEntry)
        {
            _navigation.DiscardBackEntry(Route.GrevStoreApp);
        }

        if (showCancelledStatus)
        {
            _grevStoreAppView.ShowStatus("Uninstall cancelled. Nothing was changed.");
        }
    }

    private void StoreWarningCancel_Click(object sender, RoutedEventArgs e)
    {
        HideStoreUninstallWarning(discardBackEntry: true, showCancelledStatus: true);
        FocusRouteSoon();
    }

    private void StoreWarningConfirm_Click(object sender, RoutedEventArgs e)
    {
        var package = _storeUninstallWarningPackage;
        if (package is null) return;

        HideStoreUninstallWarning(discardBackEntry: true, showCancelledStatus: false);
        _ = BeginStoreUninstallAsync(package);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_storeInstallBusy)
        {
            return;
        }

        e.Cancel = true;
        StoreProgressMessageText.Text = "This Store operation is still running. Grev Home can close after it finishes.";
    }

    private enum StoreRouteTransition
    {
        None,
        WarningPush
    }
}
