using GrevHome.Apps;
using GrevHome.Store.Installers;

namespace GrevHome.Store;

public static class MediaCenterCatalog
{
    public static IReadOnlyList<GrevStorePackageDefinition> Packages { get; } =
    [
        new(
            PackageId: "kodi",
            InstallerId: KodiInstallerService.InstallerId,
            Category: GrevStoreCategory.Media,
            App: new AppDefinition("kodi", "Kodi", AppKind.Media, InstallStrategy.GrevIdPortable,
                DataStrategy.GrevId,
                new AppLaunchDefinition("kodi.exe", "-p -fs", "{BinaryRoot}", "kodi"),
                true, "Profile-isolated ten-foot media centre for local and network media."),
            Presentation: new AppPresentationDefaults("Kodi", "#17B2E7"),
            Capabilities: AppPackageCapability.Install | AppPackageCapability.Update |
                          AppPackageCapability.Repair | AppPackageCapability.ProfileUninstall |
                          AppPackageCapability.AppSettings | AppPackageCapability.PresentationOverrides,
            RuntimePolicy: new AppRuntimePolicy(AppWindowMode.Maximized, AppWindowReturnBehavior.KeepShellHidden),
            VersionPolicy: new AppVersionPolicy(KodiInstallerService.SupportedVersion, false),
            StoreDescription: "Kodi's native ten-foot interface is fully usable with a controller. Because Kodi has no account sign-in that isolates users, Grev Home installs a separate portable Kodi and media database for every GrevID.",
            SetupNotice: "Kodi is ready for controller use after installation. Add your own local, USB or network media sources from Kodi; Grev Home does not install third-party streaming add-ons.",
            GrevHomeIntegrations:
            [
                "Official stable Kodi Windows x64 installer pinned and SHA-256 verified.",
                "Separate Kodi settings, libraries, watched state and add-ons for every GrevID.",
                "Native ten-foot controller interface launched fullscreen.",
                "No Windows startup entry; Return Home and app management remain available."
            ]),
        new(
            PackageId: "plex-htpc",
            InstallerId: PlexHtpcInstallerService.InstallerId,
            Category: GrevStoreCategory.Media,
            App: new AppDefinition("plex-htpc", "Plex HTPC", AppKind.Media, InstallStrategy.SystemInstalled,
                DataStrategy.NativeAccount,
                new AppLaunchDefinition("Plex HTPC.exe", "", null, "Plex HTPC"),
                true, "Plex's native television interface for a Windows PC connected to a TV."),
            Presentation: new AppPresentationDefaults("Plex HTPC", "#E5A00D"),
            Capabilities: AppPackageCapability.Install | AppPackageCapability.Repair |
                          AppPackageCapability.LibraryMembership | AppPackageCapability.AppSettings |
                          AppPackageCapability.PresentationOverrides | AppPackageCapability.AdminManagement,
            RuntimePolicy: new AppRuntimePolicy(AppWindowMode.Maximized, AppWindowReturnBehavior.KeepShellHidden),
            VersionPolicy: new AppVersionPolicy(PlexHtpcInstallerService.SupportedVersion, true),
            Featured: true,
            StoreDescription: "Plex HTPC is installed once for Windows and shared as a Global App. Plex owns sign-in, home users, watched state and updates; each GrevID independently chooses whether it appears in their Grev Home library.",
            SetupNotice: "Sign into Plex on first launch using Plex's own account flow. Account credentials remain under Plex's control and are never stored by Grev Home.",
            GrevHomeIntegrations:
            [
                "Official Plex HTPC Windows x64 installer pinned and SHA-256 verified.",
                "One Windows installation with per-GrevID Grev Home library membership.",
                "Native TV interface and controller/remote input retained without keyboard emulation.",
                "Plex owns account security, profiles, playback history and native updates.",
                "Windows startup entries are removed by Grev Home's Store policy."
            ])
    ];
}
