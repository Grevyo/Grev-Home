using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Presentation;
using GrevHome.Store.Installers;

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
    AppPackageCapability Capabilities,
    AppControllerProfileDefaults? ControllerProfile = null,
    AppRuntimePolicy? RuntimePolicy = null,
    AppVersionPolicy? VersionPolicy = null,
    AppOnboardingDefinition? Onboarding = null,
    bool Featured = false,
    string? StoreDescription = null,
    string? SetupNotice = null,
    IReadOnlyList<string>? GrevHomeIntegrations = null)
{
    public bool IsProfileInstall => App.InstallStrategy == InstallStrategy.GrevIdPortable;
    public AppRuntimePolicy EffectiveRuntimePolicy => RuntimePolicy ?? new AppRuntimePolicy();
}

public sealed class GrevStoreCatalogService
{
    private static readonly IReadOnlyList<GrevStorePackageDefinition> Packages =
    [
        new GrevStorePackageDefinition(
            PackageId: "retroarch",
            InstallerId: RetroArchInstallerService.InstallerId,
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
            Capabilities:
                AppPackageCapability.Install |
                AppPackageCapability.Update |
                AppPackageCapability.Repair |
                AppPackageCapability.ProfileUninstall |
                AppPackageCapability.ControllerProfile |
                AppPackageCapability.AppSettings |
                AppPackageCapability.PresentationOverrides,
            ControllerProfile: AppControllerProfileDefaults.Empty,
            RuntimePolicy: new AppRuntimePolicy(
                AppWindowMode.Normal,
                AppWindowReturnBehavior.ReturnHomeWhenMinimizedOrHidden),
            VersionPolicy: new AppVersionPolicy(
                CurrentVersion: RetroArchInstallerService.SupportedVersion,
                NativeAutoUpdate: false),
            Featured: true,
            StoreDescription: "RetroArch is a multi-system emulation frontend that can run games from many classic consoles through individual emulator cores. Grev Home installs RetroArch as a Profile App so every GrevID can keep its own emulator environment, RetroAchievements identity, settings, saves and states without conflicting with another user on the same machine.",
            GrevHomeIntegrations:
            [
                "Profile-isolated install, configuration, saves and save states for the current GrevID.",
                "Trusted update and repair can replace only this GrevID's RetroArch binaries while preserving profile data.",
                "Launch through the Grev Home runtime for Return Home, Overlay, Running Apps, restart/recovery and tracked playtime.",
                "Grev Home sessions and playtime can feed the owning profile's activity, level and stats.",
                "Controller-first discovery, launch and app management inside the permanent Grev Home shell."
            ]),

        new GrevStorePackageDefinition(
            PackageId: "pcsx2",
            InstallerId: PCSX2InstallerService.InstallerId,
            Category: GrevStoreCategory.Emulator,
            App: new AppDefinition(
                AppId: "pcsx2",
                Name: "PCSX2",
                Kind: AppKind.Emulator,
                InstallStrategy: InstallStrategy.GrevIdPortable,
                DataStrategy: DataStrategy.GrevId,
                Launch: new AppLaunchDefinition(
                    Executable: "pcsx2-qt.exe",
                    Arguments: "",
                    WorkingDirectory: "{BinaryRoot}",
                    ProcessName: "pcsx2-qt"),
                SupportsController: true,
                Description: "Profile-isolated PlayStation 2 emulator with independent configuration, BIOS selection and application data per GrevID."),
            Presentation: new AppPresentationDefaults(
                DisplayName: "PCSX2"),
            Capabilities:
                AppPackageCapability.Install |
                AppPackageCapability.Update |
                AppPackageCapability.Repair |
                AppPackageCapability.ProfileUninstall |
                AppPackageCapability.ControllerProfile |
                AppPackageCapability.ControllerGuide |
                AppPackageCapability.AppSettings |
                AppPackageCapability.PresentationOverrides,
            ControllerProfile: new AppControllerProfileDefaults(
                Enabled: true,
                Mappings: DesktopMouseMappings(
                    new(AppControllerControl.A, new(AppControllerOutputKind.KeyboardShortcut, "ENTER")),
                    new(AppControllerControl.LeftShoulder, new(AppControllerOutputKind.KeyboardShortcut, "SHIFT TAB")),
                    new(AppControllerControl.RightShoulder, new(AppControllerOutputKind.KeyboardShortcut, "TAB")))),
            RuntimePolicy: new AppRuntimePolicy(
                AppWindowMode.Maximized,
                AppWindowReturnBehavior.ReturnHomeWhenMinimizedOrHidden),
            VersionPolicy: new AppVersionPolicy(
                CurrentVersion: PCSX2InstallerService.SupportedVersion,
                NativeAutoUpdate: false),
            Onboarding: new AppOnboardingDefinition(
                Title: "PCSX2 Setup Controls",
                Summary: "Emulated keyboard and mouse controls are temporarily enabled so PCSX2 can be configured from the controller before its native gamepad setup is finished. Complete the PCSX2 setup wizard, BIOS selection and controller configuration, then disable this Grev control layer below so PCSX2 receives only its normal native controller input. You can turn it back on at any time from PCSX2 App Settings.",
                ControllerGuideControls:
                [
                    AppControllerControl.RightTrigger,
                    AppControllerControl.LeftTrigger,
                    AppControllerControl.RightStick,
                    AppControllerControl.LeftStick,
                    AppControllerControl.X,
                    AppControllerControl.A,
                    AppControllerControl.B,
                    AppControllerControl.DPadUp,
                    AppControllerControl.DPadDown,
                    AppControllerControl.DPadLeft,
                    AppControllerControl.DPadRight,
                    AppControllerControl.RightShoulder
                ],
                ControllerProfileDisplayName: "Emulated Keyboard & Mouse",
                QuickDisableControllerProfileLabel: "Disable Emulated Keyboard & Mouse",
                QuickDisableControllerProfileDescription: "Use this after PCSX2 is configured. It only disables Grev Home's temporary keyboard/mouse translation for this GrevID; it does not disable PCSX2's native controller support or delete any mappings. The same switch remains available in App Settings."),
            Featured: true,
            StoreDescription: "PCSX2 is a PlayStation 2 emulator. Grev Home installs the official Stable Windows x64 portable build as a Profile App and gives each GrevID a separate PCSX2 data path so configuration, BIOS selection, memory cards and other emulator data do not silently mix between profiles.",
            SetupNotice: "BIOS required: PCSX2 cannot run games until a PlayStation 2 BIOS dumped from a console you own has been configured. Put the BIOS files in {DataLocation}\\bios, open PCSX2 and select/configure that BIOS. Once this is done, PCSX2 is ready to run your PS2 game dumps.",
            GrevHomeIntegrations:
            [
                "Official PCSX2 Stable Windows x64 Qt portable package, pinned and SHA-256 verified before extraction.",
                "Profile-owned binaries for the current GrevID; PCSX2 Stable portable.txt redirects its DataRoot into persistent GrevID AppData.",
                "The BIOS folder is created automatically inside the GrevID PCSX2 data root; Grev Home never supplies proprietary PlayStation 2 BIOS files.",
                "Trusted update and repair replace only PCSX2 binaries while preserving the GrevID's PCSX2 data and BIOS folder.",
                "Temporary per-GrevID Grev Desktop controls make the PCSX2 first-run wizard keyboard/mouse-accessible from a controller, with a one-press disable helper once native controller setup is complete.",
                "PCSX2 native controller support remains intact while Grev Home provides Return Home, Overlay, Running Apps, App Killer and tracked playtime around the emulator."
            ]),

        new GrevStorePackageDefinition(
            PackageId: "discord",
            InstallerId: DiscordInstallerService.InstallerId,
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
            Capabilities:
                AppPackageCapability.Install |
                AppPackageCapability.Repair |
                AppPackageCapability.MachineUninstall |
                AppPackageCapability.LibraryMembership |
                AppPackageCapability.ControllerProfile |
                AppPackageCapability.ControllerGuide |
                AppPackageCapability.AppSettings |
                AppPackageCapability.PresentationOverrides |
                AppPackageCapability.AdminManagement,
            ControllerProfile: new AppControllerProfileDefaults(
                Enabled: true,
                Mappings: DesktopMouseMappings(
                    new(AppControllerControl.Y, new(AppControllerOutputKind.KeyboardShortcut, "CTRL K")),
                    new(AppControllerControl.LeftShoulder, new(AppControllerOutputKind.KeyboardShortcut, "SHIFT F6")),
                    new(AppControllerControl.RightShoulder, new(AppControllerOutputKind.KeyboardShortcut, "F6")),
                    new(AppControllerControl.Menu, new(AppControllerOutputKind.KeyboardShortcut, "CTRL SHIFT M")),
                    new(AppControllerControl.View, new(AppControllerOutputKind.KeyboardShortcut, "CTRL SHIFT D")),
                    new(AppControllerControl.LeftThumb, new(AppControllerOutputKind.KeyboardShortcut, "TAB")))),
            RuntimePolicy: new AppRuntimePolicy(
                AppWindowMode.Maximized,
                AppWindowReturnBehavior.ReturnHomeWhenMinimizedOrHidden),
            VersionPolicy: new AppVersionPolicy(
                CurrentVersion: null,
                NativeAutoUpdate: true),
            Onboarding: new AppOnboardingDefinition(
                Title: "Discord Controls",
                Summary: "Grev Home is translating your controller for Discord. These cards reflect the current resolved controller profile for this GrevID.",
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
                ]),
            Featured: true,
            StoreDescription: "Install Discord Stable for the current Windows account and launch it through Grev Home. Discord keeps its normal account, updater and AppData; Grev Home layers a per-GrevID editable controller profile over the desktop app using a Steam-inspired desktop mouse layout plus Discord's keyboard-navigation shortcuts.",
            GrevHomeIntegrations:
            [
                "Official Discord Stable Windows-user installation with native Discord self-updating left intact.",
                "Global App library membership is per GrevID even though the Windows installation is shared.",
                "Per-GrevID editable controller profile and reusable first-launch controller guide.",
                "Grev Desktop controls: right stick moves the pointer, RT left-clicks, LT right-clicks, left stick scrolls, X opens the keyboard and B sends Escape.",
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
