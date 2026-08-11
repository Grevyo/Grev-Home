using System.ComponentModel;
using System.IO;
using GrevHome.Apps;
using GrevHome.Navigation;
using GrevHome.Profiles;
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
        _adminConsoleView.UninstallRequested += entry => _ = BeginAdminMachineUninstallAsync(entry);
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

        try
        {
            var machineApps = await _installedApps.GetMachineInstalledAsync();
            var items = machineApps
                .Select(entry =>
                {
                    var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
                    var canUninstall = package is not null &&
                                       !package.IsProfileInstall &&
                                       string.Equals(package.InstallerId, DiscordInstallerService.InstallerId, StringComparison.OrdinalIgnoreCase) &&
                                       _discordInstaller is not null;
                    return new AdminMachineAppItem(entry, canUninstall);
                })
                .OrderBy(item => item.Entry.Manifest.Definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _adminConsoleView.SetApps(items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _adminConsoleView.ShowStatus($"Could not read machine app state: {ex.Message}");
        }
    }

    private async Task BeginAdminMachineUninstallAsync(InstalledAppEntry entry)
    {
        if (_storeInstallBusy || IsStoreModalOpen)
        {
            return;
        }

        if (!CanUseAdminConsole())
        {
            _adminConsoleView.ShowStatus("Machine uninstall was blocked because Admin permission is no longer active.");
            return;
        }

        var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
        if (package is null || package.IsProfileInstall)
        {
            _adminConsoleView.ShowStatus("That app is not a trusted Global App package and cannot be machine-uninstalled here.");
            return;
        }

        if (IsStorePackageRunning(package, grevId: null))
        {
            _adminConsoleView.ShowStatus($"Close {package.Presentation.DisplayName} before uninstalling it from the machine.");
            return;
        }

        _storeInstallBusy = true;
        _adminConsoleView.SetBusy(true, $"Uninstalling {package.Presentation.DisplayName} from the machine…");
        ShowStoreOperation(package, "Machine uninstall", "Preparing Admin-only package uninstall…");
        var completed = false;

        try
        {
            var progress = CreateStoreProgress(package);
            if (string.Equals(package.InstallerId, DiscordInstallerService.InstallerId, StringComparison.OrdinalIgnoreCase) &&
                _discordInstaller is not null)
            {
                await _discordInstaller.UninstallAsync(package, progress);
                completed = true;
            }
            else
            {
                throw new InvalidOperationException($"Trusted installer '{package.InstallerId}' does not support Admin machine uninstall yet.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            _adminConsoleView.ShowStatus($"Machine uninstall failed: {ex.Message}");
        }
        finally
        {
            _storeInstallBusy = false;
            HideStoreOperation();
            _adminConsoleView.SetBusy(false);
        }

        await RefreshAdminConsoleAsync();
        RefreshGrevStore();
        if (completed)
        {
            _adminConsoleView.ShowStatus($"{package.Presentation.DisplayName} was uninstalled from the Windows machine. This affects every GrevID.");
        }
    }
}
