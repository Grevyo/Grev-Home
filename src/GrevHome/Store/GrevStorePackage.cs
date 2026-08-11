using GrevHome.Apps;
using GrevHome.Input;
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
    AppControllerProfileDefaults? ControllerProfile = null,
    bool LaunchMaximized = false,
    bool ShowControllerGuideOnLaunch = false,
    IReadOnlyList<AppControllerControl>? ControllerGuideControls = null,
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
            ControllerProfile: AppControllerProfileDefaults.Empty,
            Featured: true,
            StoreDescription: "RetroArch is a multi-system emulation frontend that can run games from many classic consoles through individual emulator cores. Grev Home installs RetroArch as a Profile App so every GrevID can keep its own emulator environment, RetroAchievements identity, settings, saves and states without conflicting with another user on the same machine.",
            GrevHomeIntegrations:
            [
                "Profile-isolated install, configuration, saves and save states for the current GrevID.",
                "Launch through the Grev Home runtime for Return Home, Overlay, Running Apps, restart/recovery and tracked playtime.",
                "Grev Home sessions and playtime can feed the owning profile's activity, level and stats.",
                "RetroAchievements is designed as a profile-owned connection so achievements and game progress can feed the Grev Home profile later.",
                "Controller-first discovery, launch and app management inside the permanent Grev Home shell."
            ]),

        new GrevStorePackageDefinition(
            PackageId: "discord",
            InstallerId: "discord",
            Category: GrevStoreCategory.Application,
            App: new AppDefinition(
                AppId: "discord",
                Name: "Discord",
                Kind: AppKind.Application,
                InstallStrategy: InstallStrategy.SystemInstalled,
                DataStrategy: DataStrategy.NativeAccount,
                Launch: new AppLaunchDefinition(
                    Executable: "%LOCALAPPDATA%\\Discord\\Update.exe",
                    Arguments: "--processStart Discord.exe",
                    WorkingDirectory: "%LOCALAPPDATA%\\Discord",
                    ProcessName: "Discord",
                    SingleInstance: true),
                SupportsController: false,
                Description: "Discord desktop for text, voice and video. The Windows-user Discord account/data stays native while Grev Home adds controller-first launch, navigation and app management."),
            Presentation: new AppPresentationDefaults(
                DisplayName: "Discord",
                TileColor: "#5865F2",
                IconAsset: DefaultAppArtwork.DiscordIconAssetUri),
            ControllerProfile: new AppControllerProfileDefaults(
                Enabled: true,
                Mappings: DesktopMouseMappings(
                    new(AppControllerControl.Y, new(AppControllerOutputKind.KeyboardShortcut, "CTRL K")),
                    new(AppControllerControl.LeftShoulder, new(AppControllerOutputKind.KeyboardShortcut, "SHIFT F6")),
                    new(AppControllerControl.RightShoulder, new(AppControllerOutputKind.KeyboardShortcut, "F6")),
                    new(AppControllerControl.Menu, new(AppControllerOutputKind.KeyboardShortcut, "CTRL SHIFT M")),
                    new(AppControllerControl.View, new(AppControllerOutputKind.KeyboardShortcut, "CTRL SHIFT D")),
                    new(AppControllerControl.LeftThumb, new(AppControllerOutputKind.KeyboardShortcut, "TAB")))),
            LaunchMaximized: true,
            ShowControllerGuideOnLaunch: true,
            ControllerGuideControls:
            [
                AppControllerControl.RightTrigger,
                AppControllerControl.LeftTrigger,
                AppControllerControl.RightStick,
                AppControllerControl.LeftStick,
                AppControllerControl.X,
                AppControllerControl.B,
                AppControllerControl.Y,
                AppControllerControl.LeftShoulder,
                AppControllerControl.RightShoulder,
                AppControllerControl.Menu,
                AppControllerControl.View,
                AppControllerControl.LeftThumb
            ],
            Featured: true,
            StoreDescription: "Install Discord Stable for the current Windows account and launch it through Grev Home. Discord keeps its normal account, updater and AppData; Grev Home layers a per-GrevID editable controller profile over the desktop app using a Steam-inspired desktop mouse layout plus Discord's keyboard-navigation shortcuts.",
            GrevHomeIntegrations:
            [
                "Official Discord Stable Windows download and Windows-user installation.",
                "Per-GrevID editable controller profile even though the underlying Discord installation is shared by the Windows account.",
                "Grev Desktop controls: right stick moves the pointer, RT left-clicks, LT right-clicks, left stick scrolls, X opens the keyboard and B sends Escape.",
                "Discord shortcuts remain layered onto shoulders, Y, Menu, View and L3 without making A a dangerous generic Enter key.",
                "Launch maximized through the Grev Home runtime for Return Home, Overlay, Running Apps, App Killer, restart/recovery and tracked usage."
            ])
    ];

    public IReadOnlyList<GrevStorePackageDefinition> GetAll() => Packages;

    public GrevStorePackageDefinition? Find(string packageId) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<AppControllerMapping> DesktopMouseMappings(
        params AppControllerMapping[] appSpecificMappings)
    {
        var mappings = new Dictionary<AppControllerControl, AppControllerOutput>
        {
            [AppControllerControl.DPadUp] = new(AppControllerOutputKind.KeyboardShortcut, "UP"),
            [AppControllerControl.DPadDown] = new(AppControllerOutputKind.KeyboardShortcut, "DOWN"),
            [AppControllerControl.DPadLeft] = new(AppControllerOutputKind.KeyboardShortcut, "LEFT"),
            [AppControllerControl.DPadRight] = new(AppControllerOutputKind.KeyboardShortcut, "RIGHT"),
            [AppControllerControl.B] = new(AppControllerOutputKind.KeyboardShortcut, "ESCAPE"),
            [AppControllerControl.X] = new(AppControllerOutputKind.GrevKeyboard),
            [AppControllerControl.LeftTrigger] = new(AppControllerOutputKind.MouseRightClick),
            [AppControllerControl.RightTrigger] = new(AppControllerOutputKind.MouseLeftClick),
            [AppControllerControl.LeftStick] = new(AppControllerOutputKind.MouseScroll),
            [AppControllerControl.RightStick] = new(AppControllerOutputKind.MouseCursor)
        };

        foreach (var mapping in appSpecificMappings)
        {
            mappings[mapping.Control] = mapping.Output;
        }

        return mappings
            .Select(pair => new AppControllerMapping(pair.Key, pair.Value))
            .ToArray();
    }
}
