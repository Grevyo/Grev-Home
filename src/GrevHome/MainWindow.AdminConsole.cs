using System.ComponentModel;
using System.IO;
using System.Net.Http;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Store;
using GrevHome.Store.Installers;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly AdminConsoleView _adminConsoleView = new();
    private bool _adminConsoleIntegrationReady;

    private void InitializeAdminConsoleIntegration()
    {
        if (_adminConsoleIntegrationReady) return;
        _adminConsoleIntegrationReady = true;

        _dashboardView.AdminConsoleRequested += (_, _) => _ = OpenAdminConsoleAsync();
        _adminConsoleView.BackRequested += (_, _) => _navigation.GoBack();
        _adminConsoleView.UpdateRequested += item => _ = BeginAdminPackageOperationAsync(
            item,
            AppPackageCapability.Update,
            AppLifecycleState.Updating,
            "Updating",
            (installer, context, progress) => installer.UpdateAsync(context, progress));
        _adminConsoleView.RepairRequested += item => _ = BeginAdminPackageOperationAsync(
            item,
            AppPackageCapability.Repair,
            AppLifecycleState.Repairing,
            "Repairing",
            (installer, context, progress) => installer.RepairAsync(context, progress));
        _adminConsoleView.UninstallRequested += item => _ = BeginAdminPackageOperationAsync(
            item,
            AppPackageCapability.MachineUninstall,
            AppLifecycleState.Uninstalling,
            "Machine uninstall",
            (installer, context, progress) => installer.UninstallAsync(context, progress));
        _navigation.RouteChanged += HandleAdminConsoleRouteChanged;
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current != Route.AdminConsole)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!CanUseAdminConsole())
                {
                    _navigation.Reset(Route.Dashboard);
                    _dashboardView.ShowStatus("Admin Console closed because the Primary User no longer has Admin machine permissions.");
                    return;
                }

                _ = RefreshAdminConsoleAsync();
            }));
        };
        _runtimeSessions.SessionChanged += _ =>
        {
            if (_navigation.Current == Route.AdminConsole && !_storeInstallBusy)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshAdminConsoleAsync()));
            }
        };
        _runtimeSessions.SessionEnded += _ =>
        {
            if (_navigation.Current == Route.AdminConsole && !_storeInstallBusy)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshAdminConsoleAsync()));
            }
        };
    }

    private bool CanUseAdminConsole()
    {
        var primary = _session.PrimaryUser;
        return primary?.Role == AccountRole.Admin &&
               AccountAuthorizationService.Allows(primary.Role, AccountPermission.InstallPackages);
    }

    private async Task OpenAdminConsoleAsync()
    {
        if (!CanUseAdminConsole())
        {
            _dashboardView.ShowStatus("Admin Console requires an Admin Primary User.");
            return;
        }

        await RefreshAdminConsoleAsync();
        _navigation.Navigate(Route.AdminConsole);
    }

    private void HandleAdminConsoleRouteChanged(Route route)
    {
        if (route != Route.AdminConsole)
        {
            return;
        }

        if (!CanUseAdminConsole())
        {
            _navigation.Reset(Route.Dashboard);
            _dashboardView.ShowStatus("Admin Console requires an Admin Primary User.");
            return;
        }

        RouteHost.Content = _adminConsoleView;
        _ = RefreshAdminConsoleAsync();
        FocusRouteSoon();
    }

    private async Task RefreshAdminConsoleAsync()
    {
        if (!CanUseAdminConsole())
        {
            return;
        }

        var lifecycleService = _appLifecycle;
        var installers = _packageInstallers;
        if (lifecycleService is null || installers is null)
        {
            _adminConsoleView.ShowStatus("App platform services are not initialized yet.");
            return;
        }

        try
        {
            var machineApps = await _installedApps.GetMachineInstalledAsync();
            var items = new List<AdminMachineAppItem>();

            foreach (var entry in machineApps.OrderBy(
                         item => item.Manifest.Definition.Name,
                         StringComparer.OrdinalIgnoreCase))
            {
                var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
                AppLifecycleSnapshot? lifecycle = null;
                var libraryUsers = new List<string>();

                if (package is not null && !package.IsProfileInstall)
                {
                    lifecycle = await lifecycleService.ResolveAsync(package, grevId: null);
                    foreach (var profile in _profiles.OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (await _installedApps.IsGlobalAppInUserLibraryAsync(profile.GrevId, package.App.AppId))
                        {
                            libraryUsers.Add($"{profile.DisplayName} (@{profile.Username})");
                        }
                    }
                }

                var registered = package is not null && installers.TryGet(package.InstallerId, out _);
                var adminManageable = package?.Supports(AppPackageCapability.AdminManagement) == true;
                var running = lifecycle?.IsRunning == true;

                items.Add(new AdminMachineAppItem(
                    entry,
                    package,
                    lifecycle,
                    libraryUsers,
                    CanUpdate: registered && adminManageable &&
                               package!.Supports(AppPackageCapability.Update) &&
                               lifecycle?.UpdateAvailable == true && !running,
                    CanRepair: registered && adminManageable &&
                               package!.Supports(AppPackageCapability.Repair) && !running,
                    CanUninstall: registered && adminManageable &&
                                  package!.Supports(AppPackageCapability.MachineUninstall) && !running));
            }

            _adminConsoleView.SetApps(items);
            _adminConsoleView.ShowStatus(
                "Global App machine state is shown separately from per-GrevID library membership. Destructive actions are capability-gated and re-check Admin permission when executed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _adminConsoleView.ShowStatus($"Could not read machine app state: {ex.Message}");
        }
    }

    private async Task BeginAdminPackageOperationAsync(
        AdminMachineAppItem item,
        AppPackageCapability requiredCapability,
        AppLifecycleState operationState,
        string action,
        Func<ITrustedPackageInstaller, PackageOperationContext, IProgress<PackageInstallProgress>, Task> operation)
    {
        if (_storeInstallBusy || IsStoreModalOpen)
        {
            return;
        }

        if (!CanUseAdminConsole())
        {
            _adminConsoleView.ShowStatus("The Admin action was blocked because Admin machine permission is no longer active.");
            return;
        }

        var package = item.Package ?? _grevStoreCatalog.Find(item.Entry.Manifest.Definition.AppId);
        var installers = _packageInstallers;
        if (package is null || package.IsProfileInstall || installers is null ||
            !package.Supports(AppPackageCapability.AdminManagement) ||
            !package.Supports(requiredCapability))
        {
            _adminConsoleView.ShowStatus("That machine action is not declared by this trusted Global App package.");
            return;
        }

        if (IsStorePackageRunning(package, grevId: null))
        {
            _adminConsoleView.ShowStatus($"Close {package.Presentation.DisplayName} before changing its machine installation.");
            return;
        }

        _storeInstallBusy = true;
        _storeOperationStates[package.App.AppId] = operationState;
        _adminConsoleView.SetBusy(true, $"{action} {package.Presentation.DisplayName}…");
        ShowStoreOperation(package, action, "Preparing Admin-only trusted package operation…");
        var completed = false;

        try
        {
            var installer = installers.Require(package);
            var progress = CreateStoreProgress(package);
            await operation(
                installer,
                new PackageOperationContext(package, GrevId: null),
                progress);
            completed = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or
                                   InvalidOperationException or InvalidDataException or Win32Exception)
        {
            _adminConsoleView.ShowStatus($"{action} failed: {ex.Message}");
        }
        finally
        {
            _storeOperationStates.Remove(package.App.AppId);
            _storeInstallBusy = false;
            HideStoreOperation();
            _adminConsoleView.SetBusy(false);
        }

        await RefreshAdminConsoleAsync();
        RefreshGrevStore();
        if (_navigation.Current == Route.GrevStoreApp && _selectedStorePackage == package)
        {
            await RefreshSelectedStorePackageAsync();
        }

        if (completed)
        {
            var result = requiredCapability == AppPackageCapability.MachineUninstall
                ? $"{package.Presentation.DisplayName} was uninstalled from the Windows machine. This affects every GrevID, but per-GrevID settings remain available for a future reinstall."
                : $"{action} completed for {package.Presentation.DisplayName}.";
            _adminConsoleView.ShowStatus(result);
        }
    }
}
