using GrevHome.Apps;
using GrevHome.Store.Installers;

namespace GrevHome.Store;

public static class MediaCenterCatalog
{
    public static IReadOnlyList<ControllerMediaInstallerSpec> ControllerMediaSpecs { get; } =
    [
        new("jellyfin-media-player", "Jellyfin Media Player", "1.12.0",
            new Uri("https://github.com/jellyfin/jellyfin-media-player/releases/download/v1.12.0/JellyfinMediaPlayer-1.12.0-windows-x64.exe"),
            "JellyfinMediaPlayer-1.12.0-windows-x64.exe",
            "ad1e39a997bcaca481e54f026f04bca324aecf7a84d00da1e7c43d1eb5ee7014",
            ["Jellyfin Media Player"], ["JellyfinMediaPlayer.exe"], ["/S"],
            ["%ProgramFiles%\\Jellyfin Media Player\\JellyfinMediaPlayer.exe", "%LOCALAPPDATA%\\Jellyfin Media Player\\JellyfinMediaPlayer.exe"]),
        new("stremio", "Stremio", "5.0.24",
            new Uri("https://dl.strem.io/stremio-shell-ng/v5.0.24/StremioSetup-v5.0.24_x64.exe"),
            "StremioSetup-v5.0.24_x64.exe",
            "12dc718e02773d747f85d5aac765cf73915b63c582ea799cc4f9b880f1242842",
            ["Stremio"], ["stremio.exe"], ["/VERYSILENT", "/NORESTART", "/SP-"],
            ["%LOCALAPPDATA%\\Programs\\LNV\\Stremio-5\\stremio.exe", "%LOCALAPPDATA%\\Programs\\Stremio\\stremio.exe", "%LOCALAPPDATA%\\Stremio\\stremio.exe"])
    ];

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
            ]),
        new(
            PackageId: "jellyfin-media-player",
            InstallerId: "media-jellyfin-media-player",
            Category: GrevStoreCategory.Media,
            App: new AppDefinition("jellyfin-media-player", "Jellyfin Media Player", AppKind.Media,
                InstallStrategy.SystemInstalled, DataStrategy.NativeAccount,
                new AppLaunchDefinition("JellyfinMediaPlayer.exe", "--fullscreen", null, "JellyfinMediaPlayer"),
                true, "Controller-ready Jellyfin client for media hosted on your own Jellyfin server."),
            Presentation: new AppPresentationDefaults("Jellyfin", "#AA5CC3"),
            Capabilities: AppPackageCapability.Install | AppPackageCapability.Repair |
                          AppPackageCapability.LibraryMembership | AppPackageCapability.AppSettings |
                          AppPackageCapability.PresentationOverrides | AppPackageCapability.AdminManagement,
            RuntimePolicy: new AppRuntimePolicy(AppWindowMode.Maximized, AppWindowReturnBehavior.KeepShellHidden),
            VersionPolicy: new AppVersionPolicy("1.12.0", true),
            StoreDescription: "Jellyfin Media Player is installed once for Windows and connects to a user's own Jellyfin server. Its TV display mode supports gamepads, remotes and media keys.",
            SetupNotice: "A Jellyfin server address and account are required on first launch. Grev Home never stores those credentials.",
            GrevHomeIntegrations:
            [
                "Official Jellyfin Windows x64 installer pinned and SHA-256 verified.",
                "Native TV display mode supports controllers, remotes and media keys.",
                "Global Windows installation with per-GrevID library membership.",
                "Windows startup entries are removed by Grev Home's Store policy."
            ]),
        new(
            PackageId: "stremio",
            InstallerId: "media-stremio",
            Category: GrevStoreCategory.Media,
            App: new AppDefinition("stremio", "Stremio", AppKind.Media, InstallStrategy.SystemInstalled,
                DataStrategy.NativeAccount, new AppLaunchDefinition("stremio.exe", "", null, "stremio"),
                true, "Streaming media organiser with native gamepad navigation."),
            Presentation: new AppPresentationDefaults("Stremio", "#7B5BF2"),
            Capabilities: AppPackageCapability.Install | AppPackageCapability.Repair |
                          AppPackageCapability.LibraryMembership | AppPackageCapability.AppSettings |
                          AppPackageCapability.PresentationOverrides | AppPackageCapability.AdminManagement,
            RuntimePolicy: new AppRuntimePolicy(AppWindowMode.Maximized, AppWindowReturnBehavior.KeepShellHidden),
            VersionPolicy: new AppVersionPolicy("5.0.24", true),
            StoreDescription: "Stremio is installed once for Windows and uses its own account to synchronise a media library. Its desktop interface includes native gamepad controls.",
            SetupNotice: "Sign in through Stremio on first launch. Add-ons and the media sources they expose remain the user's responsibility; Grev Home installs no add-ons.",
            GrevHomeIntegrations:
            [
                "Official Stremio 5 Windows x64 installer pinned and SHA-256 verified.",
                "Native gamepad navigation retained without keyboard emulation.",
                "Global Windows installation with per-GrevID library membership.",
                "Windows startup entries are removed by Grev Home's Store policy."
            ])
    ];
}
