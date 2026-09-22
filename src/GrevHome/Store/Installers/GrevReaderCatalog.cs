using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Presentation;

namespace GrevHome.Store.Installers;

public static class GrevReaderCatalog
{
    public static IReadOnlyList<GrevStorePackageDefinition> Packages { get; } =
    [
        new GrevStorePackageDefinition(
            PackageId: "grevreader",
            InstallerId: GrevReaderInstallerService.InstallerId,
            Category: GrevStoreCategory.Utility,
            App: new AppDefinition(
                AppId: "grevreader",
                Name: "GrevReader",
                Kind: AppKind.Utility,
                InstallStrategy: InstallStrategy.SystemInstalled,
                DataStrategy: DataStrategy.NativeAccount,
                Launch: new AppLaunchDefinition(
                    Executable: @"C:\GrevCo\GrevReader\GrevReader.exe",
                    Arguments: "",
                    WorkingDirectory: @"C:\GrevCo\GrevReader",
                    ProcessName: "GrevReader",
                    SingleInstance: true,
                    TrackForegroundUsageOnly: true),
                SupportsController: true,
                Description: "Local-first game dialogue reader with per-character voices and a controller-operated in-game overlay."),
            Presentation: new AppPresentationDefaults(
                DisplayName: "GrevReader",
                TileColor: "#081A3A",
                IconAsset: PackageBrandingAssets.GrevReader),
            Capabilities:
                AppPackageCapability.Install |
                AppPackageCapability.Update |
                AppPackageCapability.Repair |
                AppPackageCapability.MachineUninstall |
                AppPackageCapability.LibraryMembership |
                AppPackageCapability.ControllerGuide |
                AppPackageCapability.AppSettings |
                AppPackageCapability.PresentationOverrides |
                AppPackageCapability.AdminManagement,
            RuntimePolicy: new AppRuntimePolicy(
                AppWindowMode.Normal,
                AppWindowReturnBehavior.ReturnHomeWhenMinimizedOrHidden),
            VersionPolicy: new AppVersionPolicy(
                CurrentVersion: GrevReaderInstallerService.SupportedVersion,
                NativeAutoUpdate: false),
            Onboarding: new AppOnboardingDefinition(
                Title: "GrevReader Controller Controls",
                Summary: "GrevReader has native controller support. Configure a game profile and capture area, start reading, then minimize GrevReader and launch your game. Press View + Menu together in-game to open its configuration overlay.",
                ControllerGuideControls:
                [
                    AppControllerControl.DPadUp,
                    AppControllerControl.DPadDown,
                    AppControllerControl.DPadLeft,
                    AppControllerControl.DPadRight,
                    AppControllerControl.A,
                    AppControllerControl.B,
                    AppControllerControl.X,
                    AppControllerControl.Y,
                    AppControllerControl.LeftShoulder,
                    AppControllerControl.RightShoulder,
                    AppControllerControl.View,
                    AppControllerControl.Menu
                ]),
            Featured: true,
            StoreDescription: "GrevReader reads game dialogue aloud using local Windows OCR and text-to-speech. Set a dialogue area or scan the screen, assign different installed voices to named characters, and control the reader from an Xbox-compatible controller without leaving your game.",
            SetupNotice: "Create a profile for the game, select its dialogue area, and use Test Capture before starting. Once reading is active, minimize GrevReader and launch the game from Grev Home. View + Menu opens the in-game overlay.",
            GrevHomeIntegrations:
            [
                "Trusted GrevReader release manifest and SHA-256 verified installer download.",
                "Silent machine-wide install, update, repair and uninstall managed directly from Grev Home.",
                "Native controller navigation and controller text entry; no Grev Home input translation is required.",
                "View + Menu opens GrevReader's capture-safe in-game configuration overlay.",
                "Per-Windows-user game profiles and character voices remain intact during updates and uninstall."
            ])
    ];
}
