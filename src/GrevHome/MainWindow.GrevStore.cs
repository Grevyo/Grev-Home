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
    private readonly Dictionary<string, AppLifecycleState> _storeOperationStates = new(StringComparer.OrdinalIgnoreCase);
    private GrevStorePackageDefinition? _selectedStorePackage;
    private GrevStorePackageDefinition? _storeUninstallWarningPackage;
    private RetroArchInstallerService? _retroArchInstaller;
    private PCSX2InstallerService? _pcsx2Installer;
    private SteamInstallerService? _steamInstaller;
    private DiscordInstallerService? _discordInstaller;
    private TrustedPackageInstallerRegistry? _packageInstallers;
    private AppLifecycleService? _appLifecycle;
    private StoreRouteTransition _storeRouteTransition;
    private bool _grevStoreIntegrationReady;
    private bool _storeInstallBusy;

    private bool IsStoreModalOpen => StoreModalOverlay.Visibility == Visibility.Visible;

    private void InitializeGrevStoreIntegration()
    {
        if (_grevStoreIntegrationReady) return;
        _grevStoreIntegrationReady = true;

        _retroArchInstaller = new RetroArchInstallerService(_paths, _installedApps);
        _pcsx2Installer = new PCSX2InstallerService(_paths, _installedApps);
        _steamInstaller = new SteamInstallerService(_paths, _installedApps);
        _discordInstaller = new DiscordInstallerService(_paths, _installedApps);
        _packageInstallers = new TrustedPackageInstallerRegistry(
        [
            _retroArchInstaller,
            _pcsx2Installer,
            _steamInstaller,
            _discordInstaller
        ]);
        _appLifecycle = new AppLifecycleService(_installedApps, _packageInstallers, _runtimeSessions);

        _dashboardView.StoreRequested += (_, _) => OpenGrevStore();
        _grevStoreView.PackageRequested += OpenStorePackage;
        _grevStoreAppView.DownloadRequested += package => _ = BeginStoreDownloadAsync(package);
        _grevStoreAppView.UpdateRequested += package => _ = BeginStoreUpdateAsync(package);
        _grevStoreAppView.RepairRequested += package => _ = BeginStoreRepairAsync(package);
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
        _runtimeSessions.SessionChanged += snapshot =>
        {
            if (_navigation.Current == Route.GrevStoreApp && !_storeInstallBusy)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshSelectedStorePackageAsync()));
            }
        };
        _runtimeSessions.SessionEnded += snapshot =>
        {
            if (_navigation.Current == Route.GrevStoreApp && !_storeInstallBusy)
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
                    break;
                }

                _storeRouteTransition = StoreRouteTransition.None;
                if (!_storeInstallBusy)
                {
                    _ = RefreshSelectedStorePackageAsync();
                }
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
        var lifecycleService = _appLifecycle;
        if (package is null || lifecycleService is null ||
            _navigation.Current != Route.GrevStoreApp ||
            _storeUninstallWarningPackage is not null)
        {
            return;
        }

        var primary = _session.PrimaryUser;
        var grevId = primary?.GrevId;
        _storeOperationStates.TryGetValue(package.App.AppId, out var operationState);
        var operation = _storeOperationStates.ContainsKey(package.App.AppId)
            ? operationState
            : (AppLifecycleState?)null;

        AppLifecycleSnapshot lifecycle;
        try
        {
            lifecycle = await lifecycleService.ResolveAsync(package, grevId, operation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _grevStoreAppView.ShowStatus($"Installed-state check failed: {ex.Message}");
            return;
        }

        if (_navigation.Current != Route.GrevStoreApp || _selectedStorePackage != package || _storeUninstallWarningPackage is not null)
        {
            return;
        }

        var installLocation = lifecycle.InstalledEntry?.Manifest.Definition.InstallStrategy == InstallStrategy.SystemInstalled &&
                              lifecycle.InstalledEntry is not null
            ? Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(
                  lifecycle.InstalledEntry.Manifest.Definition.Launch.Executable))
              ?? Environment.ExpandEnvironmentVariables(lifecycle.InstalledEntry.Manifest.Definition.Launch.Executable)
            : package.App.InstallStrategy switch
            {
                InstallStrategy.GrevIdPortable when string.IsNullOrWhiteSpace(grevId) =>
                    $"Profiles\\<GrevID>\\Apps\\{package.App.AppId}",
                InstallStrategy.GrevIdPortable => _paths.GetProfileAppRoot(grevId!, package.App.AppId),
                InstallStrategy.SystemInstalled =>
                    Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(package.App.Launch.Executable))
                    ?? Environment.ExpandEnvironmentVariables(package.App.Launch.Executable),
                _ => _paths.GetGlobalAppRoot(package.App.AppId)
            };

        var dataLocation = package.App.DataStrategy switch
        {
            DataStrategy.GrevId when string.IsNullOrWhiteSpace(grevId) =>
                $"Profiles\\<GrevID>\\AppData\\{package.App.AppId}",
            DataStrategy.GrevId => _paths.GetProfileAppDataRoot(grevId!, package.App.AppId),
            DataStrategy.Global => _paths.GetGlobalAppDataRoot(package.App.AppId),
            DataStrategy.NativeAccount => "Native Windows/app account",
            _ => "App managed"
        };

        _grevStoreAppView.SetPackage(package, primary, lifecycle, installLocation, dataLocation);
    }

    private async Task BeginStoreDownloadAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen) return;
        if (!package.Supports(AppPackageCapability.Install))
        {
            _grevStoreAppView.ShowStatus("This package does not declare a trusted install capability.");
            return;
        }

        var grevId = _session.PrimaryUser?.GrevId;
        if (package.IsProfileInstall && string.IsNullOrWhiteSpace(grevId))
        {
            _grevStoreAppView.ShowStatus("A persistent local Primary User is required to download this Profile App.");
            return;
        }

        var lifecycle = await ResolveLifecycleForOperationAsync(package, grevId);
        if (lifecycle is null) return;

        if (!package.IsProfileInstall && lifecycle.IsInstalled && !lifecycle.IsInCurrentUserLibrary)
        {
            if (!package.Supports(AppPackageCapability.LibraryMembership) || string.IsNullOrWhiteSpace(grevId))
            {
                _grevStoreAppView.ShowStatus("A persistent local Primary User is required to save Global App library membership.");
                return;
            }

            try
            {
                await _installedApps.RestoreGlobalAppToUserLibraryAsync(grevId, package.App.AppId);
                await RefreshSelectedStorePackageAsync();
                _grevStoreAppView.ShowStatus($"{package.Presentation.DisplayName} was added back to this GrevID's library. No download was needed.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _grevStoreAppView.ShowStatus($"Library update failed: {ex.Message}");
            }
            return;
        }

        if (lifecycle.IsInstalled)
        {
            _grevStoreAppView.ShowStatus("This package is already installed for the relevant scope.");
            return;
        }

        var completed = await RunStoreOperationAsync(
            package,
            AppLifecycleState.Installing,
            "Installing",
            "Preparing trusted installer…",
            (installer, context, progress) => installer.InstallAsync(context, progress));

        if (completed && !package.IsProfileInstall && !string.IsNullOrWhiteSpace(grevId))
        {
            await _installedApps.RestoreGlobalAppToUserLibraryAsync(grevId, package.App.AppId);
        }

        await RefreshAfterStoreOperationAsync(package,
            completed ? $"{package.Presentation.DisplayName} is installed and available in this library." : null);

        // Game launchers are expected to become the console surface immediately after their first
        // successful install/adoption. Launch through the normal Grev runtime rather than directly
        // from the installer so Big Picture/controller onboarding/session tracking all start together.
        if (completed && package.App.Kind == AppKind.GameLauncher)
        {
            await LaunchInstalledGameLauncherAfterInstallAsync(package, grevId);
        }
    }

    private async Task LaunchInstalledGameLauncherAfterInstallAsync(
        GrevStorePackageDefinition package,
        string? grevId)
    {
        try
        {
            var installed = await _installedApps.GetInstalledForUserAsync(grevId);
            var entry = installed.FirstOrDefault(candidate =>
                candidate.AvailableToCurrentUser &&
                string.Equals(candidate.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                _grevStoreAppView.ShowStatus(
                    $"{package.Presentation.DisplayName} installed successfully, but Grev Home could not resolve its new runtime entry for automatic launch. Use Open to retry.");
                return;
            }

            await LaunchInstalledAppAsync(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _grevStoreAppView.ShowStatus(
                $"{package.Presentation.DisplayName} installed successfully, but automatic launch failed: {ex.Message}");
        }
    }

    private async Task BeginStoreUpdateAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen) return;
        if (!package.Supports(AppPackageCapability.Update))
        {
            _grevStoreAppView.ShowStatus("This package does not declare a Grev Home update operation.");
            return;
        }

        var grevId = _session.PrimaryUser?.GrevId;
        var lifecycle = await ResolveLifecycleForOperationAsync(package, grevId);
        if (lifecycle is null || !lifecycle.IsInstalled)
        {
            _grevStoreAppView.ShowStatus("Install the app before updating it.");
            return;
        }
        if (lifecycle.IsRunning)
        {
            _grevStoreAppView.ShowStatus($"Close {package.Presentation.DisplayName} before updating it.");
            return;
        }
        if (!lifecycle.UpdateAvailable)
        {
            _grevStoreAppView.ShowStatus("This installation already matches the package's declared Grev Home version.");
            return;
        }

        var completed = await RunStoreOperationAsync(
            package,
            AppLifecycleState.Updating,
            "Updating",
            "Preparing package-specific update…",
            (installer, context, progress) => installer.UpdateAsync(context, progress));
        await RefreshAfterStoreOperationAsync(package,
            completed ? $"{package.Presentation.DisplayName} update completed." : null);
    }

    private async Task BeginStoreRepairAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen) return;
        if (!package.Supports(AppPackageCapability.Repair))
        {
            _grevStoreAppView.ShowStatus("This package does not declare a trusted repair operation.");
            return;
        }

        var grevId = _session.PrimaryUser?.GrevId;
        var lifecycle = await ResolveLifecycleForOperationAsync(package, grevId);
        if (lifecycle is null || !lifecycle.IsInstalled)
        {
            _grevStoreAppView.ShowStatus("Install the app before repairing it.");
            return;
        }
        if (lifecycle.IsRunning)
        {
            _grevStoreAppView.ShowStatus($"Close {package.Presentation.DisplayName} before repairing it.");
            return;
        }

        var completed = await RunStoreOperationAsync(
            package,
            AppLifecycleState.Repairing,
            "Repairing",
            "Preparing package-specific repair…",
            (installer, context, progress) => installer.RepairAsync(context, progress));
        await RefreshAfterStoreOperationAsync(package,
            completed ? $"{package.Presentation.DisplayName} repair completed." : null);
    }

    private async Task<AppLifecycleSnapshot?> ResolveLifecycleForOperationAsync(
        GrevStorePackageDefinition package,
        string? grevId)
    {
        if (_appLifecycle is null)
        {
            _grevStoreAppView.ShowStatus("The app lifecycle service is not initialized.");
            return null;
        }

        try
        {
            return await _appLifecycle.ResolveAsync(package, grevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _grevStoreAppView.ShowStatus($"Could not resolve app lifecycle: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> RunStoreOperationAsync(
        GrevStorePackageDefinition package,
        AppLifecycleState lifecycleState,
        string action,
        string initialMessage,
        Func<ITrustedPackageInstaller, PackageOperationContext, IProgress<PackageInstallProgress>, Task> operation)
    {
        var installers = _packageInstallers;
        if (installers is null)
        {
            _grevStoreAppView.ShowStatus("Trusted package installers are not initialized.");
            return false;
        }

        _storeInstallBusy = true;
        _storeOperationStates[package.App.AppId] = lifecycleState;
        ShowStoreOperation(package, action, initialMessage);
        try
        {
            var installer = installers.Require(package);
            var progress = CreateStoreProgress(package);
            var context = new PackageOperationContext(package, _session.PrimaryUser?.GrevId);
            await operation(installer, context, progress);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or
                                   InvalidOperationException or InvalidDataException or Win32Exception)
        {
            _grevStoreAppView.ShowStatus($"{action} failed: {ex.Message}");
            return false;
        }
        finally
        {
            _storeOperationStates.Remove(package.App.AppId);
            _storeInstallBusy = false;
            HideStoreOperation();
        }
    }

    private async Task RefreshAfterStoreOperationAsync(GrevStorePackageDefinition package, string? successMessage)
    {
        if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
        {
            await RefreshSelectedStorePackageAsync();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                _grevStoreAppView.ShowStatus(successMessage);
            }
        }
        RefreshGrevStore();
    }

    private async Task RemoveGlobalAppFromLibraryAsync(GrevStorePackageDefinition package)
    {
        if (_storeInstallBusy || IsStoreModalOpen || package.IsProfileInstall) return;
        if (!package.Supports(AppPackageCapability.LibraryMembership))
        {
            _grevStoreAppView.ShowStatus("This Global App does not declare per-GrevID library membership.");
            return;
        }

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

        if (!package.Supports(AppPackageCapability.ProfileUninstall))
        {
            _grevStoreAppView.ShowStatus("This Profile App does not declare a trusted uninstall operation.");
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
        if (!package.Supports(AppPackageCapability.ProfileUninstall))
        {
            _grevStoreAppView.ShowStatus("This Profile App does not declare a trusted uninstall operation.");
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

        var completed = await RunStoreOperationAsync(
            package,
            AppLifecycleState.Uninstalling,
            "Uninstalling",
            "Preparing package-specific uninstall…",
            (installer, context, progress) => installer.UninstallAsync(context, progress));

        await RefreshAfterStoreOperationAsync(package,
            completed ? $"{package.Presentation.DisplayName} binaries were uninstalled for this GrevID." : null);
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
