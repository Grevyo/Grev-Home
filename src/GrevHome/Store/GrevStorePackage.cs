using GrevHome.Apps;

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
    string FallbackGlyph,
    string? IconAsset = null,
    string? TileMediaAsset = null,
    string? HeroMediaAsset = null);

public sealed record GrevStorePackageDefinition(
    string PackageId,
    string InstallerId,
    GrevStoreCategory Category,
    AppDefinition App,
    AppPresentationDefaults Presentation,
    bool Featured = false)
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
                    WorkingDirectory: "{BinaryRoot}",
                    ProcessName: "retroarch"),
                SupportsController: true,
                Description: "Profile-isolated multi-system emulator frontend with independent configuration, saves and RetroAchievements identity per GrevID."),
            Presentation: new AppPresentationDefaults(
                DisplayName: "RetroArch",
                FallbackGlyph: "RA"),
            Featured: true)
    ];

    public IReadOnlyList<GrevStorePackageDefinition> GetAll() => Packages;

    public GrevStorePackageDefinition? Find(string packageId) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
}
