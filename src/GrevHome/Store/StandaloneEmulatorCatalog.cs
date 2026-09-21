using System.Text;
using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Presentation;
using GrevHome.Store.Installers;

namespace GrevHome.Store;

public static class StandaloneEmulatorCatalog
{
    public static readonly IReadOnlyList<StandaloneEmulatorPackageSpec> Specs =
    [
        Spec("dolphin", "Dolphin", "2606a", "https://dl.dolphin-emu.org/releases/2606a/dolphin-2606a-x64.7z",
            "dolphin-2606a-x64.7z", "4c58045f9821cb63913f4df08ea86ece3cdda9f9e646154516000fa1547e0c37", "Dolphin.exe", "--user \"{DataRoot}\"", ["User"]),
        Spec("azahar", "Azahar", "2126.1.1", "https://github.com/azahar-emu/azahar/releases/download/2126.1.1/azahar-windows-msvc-2126.1.1.zip",
            "azahar-windows-msvc-2126.1.1.zip", "d65f8b710080ea839f9f35a99bd7cc9c4476d20c3abd701ae0786e1f4e175125", "azahar.exe", "", ["user"]),
        Spec("rpcs3", "RPCS3", "0.0.42-20021", "https://github.com/RPCS3/rpcs3-binaries-win/releases/download/build-a90c841b39e005e50b9fccaf5bb2a6e1d0dc4738/rpcs3-v0.0.42-20021-a90c841b_win64_msvc.7z",
            "rpcs3-v0.0.42-20021-win64.7z", "883d72545cc7a73c178d497118e77d125a9e4b22cc236ad1fdd2196049a711da", "rpcs3.exe", "", ["config", "dev_hdd0", "dev_hdd1", "games.yml", "GuiConfigs"]),
        Spec("cemu", "Cemu", "2.6", "https://github.com/cemu-project/Cemu/releases/download/v2.6/cemu-2.6-windows-x64.zip",
            "cemu-2.6-windows-x64.zip", "a6bcc2bc42a362d10213819948f3152fae7d47f70067f25939b51d3ddcfb0896", "Cemu.exe", "", ["settings.xml", "mlc01", "controllerProfiles", "graphicPacks"]),
        Spec("xenia", "Xenia Canary", "aee0871", "https://github.com/xenia-canary/xenia-canary/releases/download/aee0871/xenia_canary_windows.7z",
            "xenia_canary_windows.7z", "35bc3afbbf7ad8d956871943c2579a2aef50665eadf22d264169b4e45a61c7a6", "xenia_canary.exe", "", ["content", "xenia-canary.config.toml"]),
        Spec("xemu", "xemu", "0.8.136", "https://github.com/xemu-project/xemu/releases/download/v0.8.136/xemu-0.8.136-windows-x86_64.zip",
            "xemu-0.8.136-windows-x86_64.zip", "b25a6c24a2c2c36a0843a153cd9ee59ca6833ef87bdcd855ba0824930e4ddd1d", "xemu.exe", "", ["xemu.toml"]),
        Spec("vita3k", "Vita3K", "continuous-2026-09-17", "https://github.com/Vita3K/Vita3K/releases/download/continuous/windows-latest.zip",
            "vita3k-windows-2026-09-17.zip", "3163443c42e655d80529ca4166e9553e1d66be7fbc424c9dbcbbf0b1b53e45b8", "Vita3K.exe", "--pref-path \"{DataRoot}\"", ["config.yml"])
    ];

    public static readonly IReadOnlyList<GrevStorePackageDefinition> Packages = Specs.Select(CreatePackage).ToArray();

    private static StandaloneEmulatorPackageSpec Spec(string id, string name, string version, string uri,
        string file, string sha, string executable, string arguments, IReadOnlyList<string> mutable) =>
        new(id, name, version, new Uri(uri), file, sha, executable, arguments, mutable,
            (binaryRoot, dataRoot, gamesRoot, biosRoot) => Configure(id, binaryRoot, dataRoot, gamesRoot, biosRoot));

    private static GrevStorePackageDefinition CreatePackage(StandaloneEmulatorPackageSpec spec)
    {
        var requiresInteractiveSystemSetup = spec.AppId is "rpcs3" or "xemu" or "vita3k";
        var notice = spec.AppId switch
        {
            "rpcs3" => "RPCS3 requires Sony's official PlayStation 3 system software. Grev Home configures the profile and game paths, but firmware installation remains a one-time user action.",
            "xemu" => "xemu requires user-owned MCPX, flash BIOS and hard-disk image files. Put them in the Grev Home BIOS folder, then select them once in xemu.",
            "vita3k" => "Vita3K requires official PlayStation Vita firmware packages. Grev Home prepares the isolated profile, but firmware installation remains a one-time user action.",
            "cemu" => "Cemu is preconfigured for the shared Games and BIOS locations. Some encrypted Wii U dumps require user-owned keys.",
            "azahar" => "Azahar is preconfigured for Grev Home's game location. Encrypted 3DS content may require user-owned system keys.",
            _ => $"{spec.DisplayName} is silently installed and connected to Grev Home's shared Games and BIOS locations. Open it only for optional custom configuration."
        };

        return new GrevStorePackageDefinition(
            PackageId: spec.AppId,
            InstallerId: "standalone-" + spec.AppId,
            Category: GrevStoreCategory.Emulator,
            App: new AppDefinition(spec.AppId, spec.DisplayName, AppKind.Emulator,
                InstallStrategy.GrevIdPortable, DataStrategy.GrevId,
                new AppLaunchDefinition(spec.ExecutableName, spec.BaseArguments, "{BinaryRoot}",
                    Path.GetFileNameWithoutExtension(spec.ExecutableName)), true,
                $"Profile-isolated {spec.DisplayName} emulator managed by Grev Home."),
            Presentation: new AppPresentationDefaults(
                spec.DisplayName,
                ColorFor(spec.AppId),
                PackageBrandingAssets.ForApp(spec.AppId)),
            Capabilities: AppPackageCapability.Install | AppPackageCapability.Update | AppPackageCapability.Repair |
                          AppPackageCapability.ProfileUninstall | AppPackageCapability.AppSettings |
                          AppPackageCapability.PresentationOverrides |
                          (requiresInteractiveSystemSetup
                              ? AppPackageCapability.ControllerProfile | AppPackageCapability.ControllerGuide
                              : AppPackageCapability.None),
            ControllerProfile: requiresInteractiveSystemSetup ? SetupControllerProfile() : AppControllerProfileDefaults.Empty,
            RuntimePolicy: new AppRuntimePolicy(AppWindowMode.Maximized, AppWindowReturnBehavior.KeepShellHidden),
            VersionPolicy: new AppVersionPolicy(spec.Version, false),
            Onboarding: requiresInteractiveSystemSetup ? SetupOnboarding(spec.DisplayName) : null,
            StoreDescription: $"Grev Home installs the official {spec.DisplayName} portable Windows build separately for each GrevID, preserving that profile's configuration and emulator data across trusted updates.",
            SetupNotice: notice,
            GrevHomeIntegrations:
            [
                "Silent verified portable installation with no third-party setup wizard.",
                "Shared Grev Home Games and BIOS locations recorded during installation.",
                "Per-GrevID configuration and mutable emulator data are preserved across update and repair.",
                "Direct game launching through Grev Home with Return Home, Overlay and tracked playtime.",
                "Windows startup entries are removed by Grev Home's Store policy."
            ]);
    }

    private static void Configure(string appId, string binaryRoot, string dataRoot, string gamesRoot, string biosRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var appGamesRoot = Path.Combine(gamesRoot, appId switch
        {
            "dolphin" => "GameCube",
            "azahar" => "3DS",
            "rpcs3" => "PS3",
            "cemu" => "Wii U",
            "xenia" => "Xbox 360",
            "xemu" => "Xbox",
            "vita3k" => "PS Vita",
            _ => appId
        });
        Directory.CreateDirectory(appGamesRoot);
        File.WriteAllText(Path.Combine(dataRoot, "grevhome-paths.ini"),
            $"[GrevHome]{Environment.NewLine}Games={appGamesRoot}{Environment.NewLine}BIOS={biosRoot}{Environment.NewLine}",
            new UTF8Encoding(false));

        switch (appId)
        {
            case "dolphin":
                var dolphinConfig = Path.Combine(dataRoot, "Config");
                Directory.CreateDirectory(dolphinConfig);
                WriteIfMissing(Path.Combine(dolphinConfig, "Dolphin.ini"),
                    $"[General]{Environment.NewLine}ISOPaths = 2{Environment.NewLine}ISOPath0 = {appGamesRoot}{Environment.NewLine}ISOPath1 = {Path.Combine(gamesRoot, "Wii")}{Environment.NewLine}RecursiveISOPaths = True{Environment.NewLine}AnalyticsPermissionAsked = True{Environment.NewLine}[Display]{Environment.NewLine}Fullscreen = True{Environment.NewLine}");
                Directory.CreateDirectory(Path.Combine(gamesRoot, "Wii"));
                WriteIfMissing(Path.Combine(dolphinConfig, "GCPadNew.ini"),
                    "[GCPad1]" + Environment.NewLine +
                    "Device = XInput/0/Gamepad" + Environment.NewLine +
                    "Buttons/A = `Button A`" + Environment.NewLine +
                    "Buttons/B = `Button B`" + Environment.NewLine +
                    "Buttons/X = `Button X`" + Environment.NewLine +
                    "Buttons/Y = `Button Y`" + Environment.NewLine +
                    "Buttons/Z = `Shoulder R`" + Environment.NewLine +
                    "Buttons/Start = `Start`" + Environment.NewLine +
                    "Main Stick/Up = `Left Y+`" + Environment.NewLine +
                    "Main Stick/Down = `Left Y-`" + Environment.NewLine +
                    "Main Stick/Left = `Left X-`" + Environment.NewLine +
                    "Main Stick/Right = `Left X+`" + Environment.NewLine +
                    "C-Stick/Up = `Right Y+`" + Environment.NewLine +
                    "C-Stick/Down = `Right Y-`" + Environment.NewLine +
                    "C-Stick/Left = `Right X-`" + Environment.NewLine +
                    "C-Stick/Right = `Right X+`" + Environment.NewLine +
                    "Triggers/L = `Trigger L`" + Environment.NewLine +
                    "Triggers/R = `Trigger R`" + Environment.NewLine +
                    "D-Pad/Up = `Pad N`" + Environment.NewLine +
                    "D-Pad/Down = `Pad S`" + Environment.NewLine +
                    "D-Pad/Left = `Pad W`" + Environment.NewLine +
                    "D-Pad/Right = `Pad E`" + Environment.NewLine);
                break;
            case "azahar":
                var azaharConfig = Path.Combine(binaryRoot, "user", "config");
                Directory.CreateDirectory(azaharConfig);
                WriteIfMissing(Path.Combine(azaharConfig, "qt-config.ini"),
                    $"[UI]{Environment.NewLine}Paths\\gamedirs\\size=1{Environment.NewLine}Paths\\gamedirs\\1\\path={appGamesRoot.Replace("\\", "/")}{Environment.NewLine}Paths\\gamedirs\\1\\deep_scan=true{Environment.NewLine}fullscreen=true{Environment.NewLine}");
                break;
            case "cemu":
                WriteIfMissing(Path.Combine(binaryRoot, "settings.xml"),
                    $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{Environment.NewLine}<content><fullscreen>true</fullscreen><GamePaths><Entry>{System.Security.SecurityElement.Escape(appGamesRoot)}</Entry></GamePaths></content>{Environment.NewLine}");
                Directory.CreateDirectory(Path.Combine(binaryRoot, "mlc01"));
                break;
            case "xenia":
                WriteIfMissing(Path.Combine(binaryRoot, "portable.txt"), string.Empty);
                break;
            case "xemu":
                // xemu treats an xemu.toml beside the executable as portable mode and keeps its
                // EEPROM plus user-selected MCPX/flash/HDD paths with this GrevID-owned package.
                WriteIfMissing(Path.Combine(binaryRoot, "xemu.toml"), string.Empty);
                break;
        }
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static AppControllerProfileDefaults SetupControllerProfile() => new(true,
    [
        new(AppControllerControl.DPadUp, new(AppControllerOutputKind.KeyboardShortcut, "UP")),
        new(AppControllerControl.DPadDown, new(AppControllerOutputKind.KeyboardShortcut, "DOWN")),
        new(AppControllerControl.DPadLeft, new(AppControllerOutputKind.KeyboardShortcut, "LEFT")),
        new(AppControllerControl.DPadRight, new(AppControllerOutputKind.KeyboardShortcut, "RIGHT")),
        new(AppControllerControl.A, new(AppControllerOutputKind.KeyboardShortcut, "ENTER")),
        new(AppControllerControl.B, new(AppControllerOutputKind.KeyboardShortcut, "ESCAPE")),
        new(AppControllerControl.X, new(AppControllerOutputKind.GrevKeyboard)),
        new(AppControllerControl.LeftTrigger, new(AppControllerOutputKind.MouseRightClick)),
        new(AppControllerControl.RightTrigger, new(AppControllerOutputKind.MouseLeftClick)),
        new(AppControllerControl.LeftStick, new(AppControllerOutputKind.MouseScroll)),
        new(AppControllerControl.RightStick, new(AppControllerOutputKind.MouseCursor))
    ]);

    private static AppOnboardingDefinition SetupOnboarding(string displayName) => new(
        $"{displayName} System Setup Controls",
        $"Grev Home's temporary keyboard and mouse controller layer is enabled so {displayName}'s user-owned system-file setup can be completed without a physical keyboard or mouse. Disable it below when setup is complete so the emulator receives normal native controller input.",
        [AppControllerControl.RightStick, AppControllerControl.RightTrigger, AppControllerControl.LeftTrigger,
            AppControllerControl.LeftStick, AppControllerControl.DPadUp, AppControllerControl.DPadDown,
            AppControllerControl.DPadLeft, AppControllerControl.DPadRight, AppControllerControl.A,
            AppControllerControl.B, AppControllerControl.X],
        ControllerProfileDisplayName: "Temporary Setup Controls",
        QuickDisableControllerProfileLabel: "Finish Setup and Use Native Controller",
        QuickDisableControllerProfileDescription: "Disables only Grev Home's temporary mouse and keyboard translation for this GrevID. The emulator's native controller input remains enabled and the setup controls can be restored from App Settings.");

    private static string ColorFor(string id) => id switch
    {
        "dolphin" => "#4C8BC9", "azahar" => "#5C4BB6", "rpcs3" => "#111827",
        "cemu" => "#2B579A", "xenia" => "#202020", "xemu" => "#107C10", "vita3k" => "#1428A0",
        _ => "#151923"
    };
}
