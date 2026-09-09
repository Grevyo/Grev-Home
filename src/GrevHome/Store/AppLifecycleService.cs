using GrevHome.Apps;
using GrevHome.Runtime;
using GrevHome.Store.Installers;

namespace GrevHome.Store;

/// <summary>
/// Resolves the common Grev Home application lifecycle without teaching Store/Admin UI about
/// package-specific installers. Machine installation and per-GrevID library membership are kept
/// deliberately separate for Global Apps.
/// </summary>
public sealed class AppLifecycleService
{
    private readonly InstalledAppService _installedApps;
    private readonly TrustedPackageInstallerRegistry _installers;
    private readonly RuntimeSessionManager _runtimeSessions;

    public AppLifecycleService(
        InstalledAppService installedApps,
        TrustedPackageInstallerRegistry installers,
        RuntimeSessionManager runtimeSessions)
    {
        _installedApps = installedApps;
        _installers = installers;
        _runtimeSessions = runtimeSessions;
    }

    public async Task<AppLifecycleSnapshot> ResolveAsync(
        GrevStorePackageDefinition package,
        string? grevId,
        AppLifecycleState? operationState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        InstalledAppEntry? installedEntry;
        bool isInLibrary;

        if (package.IsProfileInstall)
        {
            installedEntry = string.IsNullOrWhiteSpace(grevId)
                ? null
                : (await _installedApps.GetInstalledForUserAsync(grevId, cancellationToken))
                    .FirstOrDefault(entry =>
                        string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase));
            isInLibrary = installedEntry is not null;
        }
        else
        {
            installedEntry = (await _installedApps.GetMachineInstalledAsync(cancellationToken))
                .FirstOrDefault(entry =>
                    string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase));
            isInLibrary = installedEntry is not null &&
                          await _installedApps.IsGlobalAppInUserLibraryAsync(grevId, package.App.AppId, cancellationToken);
        }

        var installed = installedEntry is not null;
        var running = installed && _runtimeSessions.GetActiveSessions().Any(session =>
            string.Equals(session.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
            (!package.IsProfileInstall || string.Equals(session.PrimaryGrevId, grevId, StringComparison.OrdinalIgnoreCase)));

        var health = new PackageHealthSnapshot(
            installed ? PackageHealthState.Unknown : PackageHealthState.Healthy,
            installed ? "No package-specific health inspection is available." : "Not installed.");

        if (installed && _installers.TryGet(package.InstallerId, out var installer))
        {
            try
            {
                health = await installer.InspectAsync(
                    new PackageOperationContext(package, grevId),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                health = new PackageHealthSnapshot(
                    PackageHealthState.RepairRecommended,
                    $"Health inspection failed: {ex.Message}");
            }
        }

        var repairNeeded = installed && health.State == PackageHealthState.RepairRecommended;
        var targetVersion = package.VersionPolicy?.CurrentVersion;
        var updateAvailable = installed &&
                              package.Supports(AppPackageCapability.Update) &&
                              !string.IsNullOrWhiteSpace(targetVersion) &&
                              !string.Equals(installedEntry!.Manifest.Version, targetVersion, StringComparison.OrdinalIgnoreCase);

        var state = operationState ?? ResolveStableState(
            installed,
            isInLibrary,
            running,
            updateAvailable,
            repairNeeded);

        return new AppLifecycleSnapshot(
            package,
            installedEntry,
            state,
            installed,
            isInLibrary,
            running,
            updateAvailable,
            repairNeeded,
            health,
            operationState);
    }

    private static AppLifecycleState ResolveStableState(
        bool installed,
        bool inLibrary,
        bool running,
        bool updateAvailable,
        bool repairNeeded)
    {
        if (!installed) return AppLifecycleState.NotInstalled;
        if (!inLibrary) return AppLifecycleState.RemovedFromLibrary;
        if (repairNeeded) return AppLifecycleState.RepairNeeded;
        if (updateAvailable) return AppLifecycleState.UpdateAvailable;
        if (running) return AppLifecycleState.Running;
        return AppLifecycleState.Installed;
    }
}
