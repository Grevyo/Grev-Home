using GrevHome.Apps;
using GrevHome.Presentation;

namespace GrevHome.Store;

public enum GrevStoreCategory
{
    Gaming,
    Emulator,
    Application,
    Media,
    Utility
}

public sealed record AppPresentationDefaults(
    string DisplayName,
    string TileColor = "#151923",
    string? IconAsset = null,
    string? TileMediaAsset = null,
    string? HeroMediaAsset = null);

public sealed record GrevStorePackageDefinition(
    string PackageId,
    string InstallerId,
    GrevStoreCategory Category,
    AppDefinition App,
    AppPresentationDefaults Presentation,
    bool Featured = false,
    string? StoreDescription = null,
    IReadOnlyList<string>? GrevHomeIntegrations = null)
{
    public bool IsProfileInstall => App.InstallStrategy == InstallStrategy.GrevIdPortable;
}

public sealed class GrevStoreCatalogService
{
    private static readonly IReadOnlyList<GrevStorePackageDefinition> Packages =
    [
        new GrevStorePackageDefinition(
            PackageId: "retroarch",
            InstallerId: "retroarch",
            Category: GrevStoreCategory.Emulator,
            App: new AppDefinition(
                AppId: "retroarch",
                Name: "RetroArch",
                Kind: AppKind.Emulator,
                InstallStrategy: InstallStrategy.GrevIdPortable,
                DataStrategy: DataStrategy.GrevId,
                Launch: new AppLaunchDefinition(
                    Executable: "retroarch.exe",
                    Arguments: "-c \"{DataRoot}\\retroarch.cfg\"",
                    WorkingDirectory: "{BinaryRoot}",
                    ProcessName: "retroarch"),
                SupportsController: true,
                Description: "Profile-isolated multi-system emulator frontend with independent configuration, saves and RetroAchievements identity per GrevID."),
            Presentation: new AppPresentationDefaults(
                DisplayName: "RetroArch",
                TileColor: "#000000",
                IconAsset: DefaultAppArtwork.RetroArchIconAssetUri),
            Featured: true,
            StoreDescription: "RetroArch is a multi-system emulation frontend that can run games from many classic consoles through individual emulator cores. Grev Home installs RetroArch as a Profile App so every GrevID can keep its own emulator environment, RetroAchievements identity, settings, saves and states without conflicting with another user on the same machine.",
            GrevHomeIntegrations:
            [
                "Profile-isolated install, configuration, saves and save states for the current GrevID.",
                "Launch through the Grev Home runtime for Return Home, Overlay, Running Apps, restart/recovery and tracked playtime.",
                "Grev Home sessions and playtime can feed the owning profile's activity, level and stats.",
                "RetroAchievements is designed as a profile-owned connection so achievements and game progress can feed the Grev Home profile later.",
                "Controller-first discovery, launch and app management inside the permanent Grev Home shell."
            ])
    ];

    public IReadOnlyList<GrevStorePackageDefinition> GetAll() => Packages;

    public GrevStorePackageDefinition? Find(string packageId) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
}
