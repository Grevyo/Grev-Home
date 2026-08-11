using GrevHome.Apps;
using GrevHome.Input;

namespace GrevHome.Store;

[Flags]
public enum AppPackageCapability
{
    None = 0,
    Install = 1 << 0,
    Update = 1 << 1,
    Repair = 1 << 2,
    ProfileUninstall = 1 << 3,
    MachineUninstall = 1 << 4,
    LibraryMembership = 1 << 5,
    ControllerProfile = 1 << 6,
    ControllerGuide = 1 << 7,
    AppSettings = 1 << 8,
    PresentationOverrides = 1 << 9,
    AdminManagement = 1 << 10
}

public static class AppPackageCapabilityExtensions
{
    public static bool Supports(this GrevStorePackageDefinition package, AppPackageCapability capability) =>
        (package.Capabilities & capability) == capability;
}

public enum AppLifecycleState
{
    NotInstalled,
    Installed,
    RemovedFromLibrary,
    Running,
    UpdateAvailable,
    RepairNeeded,
    Installing,
    Updating,
    Repairing,
    Uninstalling
}

public enum AppWindowMode
{
    Normal,
    Maximized
}

public enum AppWindowReturnBehavior
{
    KeepShellHidden,
    ReturnHomeWhenMinimizedOrHidden
}

/// <summary>
/// Shell/window behaviour for a managed app. Process/session reuse is deliberately not duplicated
/// here: AppLaunchDefinition.SingleInstance is the single runtime source of truth consumed by
/// RuntimeSessionManager.
/// </summary>
public sealed record AppRuntimePolicy(
    AppWindowMode WindowMode = AppWindowMode.Normal,
    AppWindowReturnBehavior ReturnBehavior = AppWindowReturnBehavior.ReturnHomeWhenMinimizedOrHidden);

public sealed record AppVersionPolicy(
    string? CurrentVersion = null,
    bool NativeAutoUpdate = false);

public sealed record AppOnboardingDefinition(
    string Title,
    string Summary,
    IReadOnlyList<AppControllerControl> ControllerGuideControls,
    bool ShowOnFirstLaunch = true);

public enum PackageHealthState
{
    Unknown,
    Healthy,
    RepairRecommended
}

public sealed record PackageHealthSnapshot(
    PackageHealthState State,
    string Message,
    string? DetectedVersion = null);

public sealed record AppLifecycleSnapshot(
    GrevStorePackageDefinition Package,
    InstalledAppEntry? InstalledEntry,
    AppLifecycleState State,
    bool IsInstalled,
    bool IsInCurrentUserLibrary,
    bool IsRunning,
    bool UpdateAvailable,
    bool RepairNeeded,
    PackageHealthSnapshot Health,
    AppLifecycleState? OperationState = null)
{
    public bool CanOpen => IsInstalled && IsInCurrentUserLibrary && InstalledEntry?.AvailableToCurrentUser == true;
}
