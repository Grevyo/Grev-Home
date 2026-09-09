using GrevHome.Apps;

namespace GrevHome.Games;

public sealed class GameLaunchResolver
{
    public InstalledAppEntry Resolve(
        GameLibraryEntry game,
        IReadOnlyList<InstalledAppEntry> installedApps,
        string grevId)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(installedApps);

        if (!GameLibraryService.IsSourceAvailable(game))
        {
            throw new FileNotFoundException("The game file is missing or the drive is unavailable.", game.SourcePath);
        }

        return game.Platform switch
        {
            GamePlatform.PlayStation2 => ResolvePlayStation2(game, installedApps, grevId),
            _ => ResolveRetroArch(game, installedApps, grevId)
        };
    }

    private static InstalledAppEntry ResolveRetroArch(
        GameLibraryEntry game,
        IReadOnlyList<InstalledAppEntry> installedApps,
        string grevId)
    {
        var retroArch = installedApps.FirstOrDefault(entry =>
            string.Equals(entry.Manifest.Definition.AppId, "retroarch", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase));
        if (retroArch is null)
        {
            throw new InvalidOperationException(
                "RetroArch is not installed for this GrevID. Install RetroArch from Grev Store before opening this game.");
        }
        if (!retroArch.AvailableToCurrentUser)
        {
            throw new InvalidOperationException(
                retroArch.AvailabilityMessage ?? "RetroArch is installed but unavailable to this GrevID.");
        }

        var coresRoot = Path.Combine(retroArch.BinaryRoot, "cores");
        var core = GetPreferredRetroArchCores(game.Platform)
            .Select(name => Path.Combine(coresRoot, name + "_libretro.dll"))
            .FirstOrDefault(File.Exists);
        if (core is null)
        {
            throw new InvalidOperationException(
                $"No compatible RetroArch core is installed for {GameLibraryService.GetPlatformDisplayName(game.Platform)}. Open RetroArch's Core Downloader and install a suitable core first.");
        }

        var baseDefinition = retroArch.Manifest.Definition;
        var launch = baseDefinition.Launch;
        var definition = new AppDefinition(
            AppId: game.GameId,
            Name: game.DisplayName,
            Kind: AppKind.GameLauncher,
            InstallStrategy: InstallStrategy.GrevIdPortable,
            DataStrategy: DataStrategy.GrevId,
            Launch: new AppLaunchDefinition(
                Executable: launch.Executable,
                Arguments: $"-L {QuoteArgument(core)} -f {QuoteArgument(game.SourcePath)}",
                WorkingDirectory: launch.WorkingDirectory,
                ProcessName: launch.ProcessName,
                SingleInstance: false,
                AdditionalProcessNames: launch.AdditionalProcessNames,
                TrackDescendantProcesses: launch.TrackDescendantProcesses,
                ForceKillEntireProcessTree: launch.ForceKillEntireProcessTree),
            SupportsController: true,
            Description: $"{GameLibraryService.GetPlatformDisplayName(game.Platform)} game launched through RetroArch.");
        return new InstalledAppEntry(
            new InstalledAppManifest(definition, retroArch.Manifest.Version, game.AddedAtUtc, grevId),
            retroArch.BinaryRoot,
            retroArch.DataRoot,
            true,
            null);
    }

    private static IReadOnlyList<string> GetPreferredRetroArchCores(GamePlatform platform) => platform switch
    {
        GamePlatform.Arcade => ["fbneo", "mame2003_plus", "mame"],
        GamePlatform.Atari2600 => ["stella2014", "stella"],
        GamePlatform.Atari5200 => ["a5200"],
        GamePlatform.Atari7800 => ["prosystem"],
        GamePlatform.AtariJaguar => ["virtualjaguar"],
        GamePlatform.AtariLynx => ["handy", "mednafen_lynx"],
        GamePlatform.NintendoEntertainmentSystem => ["mesen", "fceumm", "nestopia"],
        GamePlatform.SuperNintendo => ["snes9x", "mesen-s", "bsnes"],
        GamePlatform.Nintendo64 => ["mupen64plus_next", "parallel_n64"],
        GamePlatform.GameBoy or GamePlatform.GameBoyColor => ["gambatte", "sameboy", "mgba"],
        GamePlatform.GameBoyAdvance => ["mgba", "vba_next"],
        GamePlatform.NintendoDS => ["melonds", "desmume"],
        GamePlatform.Nintendo3DS => ["citra", "citra_canary"],
        GamePlatform.GameCube or GamePlatform.Wii => ["dolphin"],
        GamePlatform.SegaMasterSystem or GamePlatform.SegaGameGear => ["genesis_plus_gx", "picodrive"],
        GamePlatform.SegaGenesis => ["genesis_plus_gx", "picodrive"],
        GamePlatform.SegaCD => ["genesis_plus_gx", "picodrive"],
        GamePlatform.Sega32X => ["picodrive"],
        GamePlatform.SegaSaturn => ["beetle_saturn", "kronos", "yabause"],
        GamePlatform.SegaDreamcast => ["flycast"],
        GamePlatform.PlayStation => ["swanstation", "beetle_psx_hw", "beetle_psx", "pcsx_rearmed"],
        GamePlatform.PlayStationPortable => ["ppsspp"],
        GamePlatform.PcEngine or GamePlatform.PcEngineCD => ["mednafen_pce_fast", "mednafen_pce"],
        GamePlatform.NeoGeoPocket => ["mednafen_ngp"],
        GamePlatform.WonderSwan => ["mednafen_wswan"],
        GamePlatform.Commodore64 => ["vice_x64sc", "vice_x64"],
        GamePlatform.CommodoreAmiga => ["puae"],
        _ => Array.Empty<string>()
    };

    private static InstalledAppEntry ResolvePlayStation2(
        GameLibraryEntry game,
        IReadOnlyList<InstalledAppEntry> installedApps,
        string grevId)
    {
        var pcsx2 = installedApps.FirstOrDefault(entry =>
            string.Equals(entry.Manifest.Definition.AppId, "pcsx2", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase));

        if (pcsx2 is null)
        {
            throw new InvalidOperationException(
                "PCSX2 is not installed for this GrevID. Install PCSX2 from Grev Store before opening PlayStation 2 games.");
        }
        if (!pcsx2.AvailableToCurrentUser)
        {
            throw new InvalidOperationException(
                pcsx2.AvailabilityMessage ?? "PCSX2 is installed but is not currently available to this GrevID.");
        }

        var emulator = pcsx2.Manifest.Definition;
        var launch = emulator.Launch;
        var quotedGamePath = QuoteArgument(game.SourcePath);
        var definition = new AppDefinition(
            AppId: game.GameId,
            Name: game.DisplayName,
            Kind: AppKind.GameLauncher,
            InstallStrategy: InstallStrategy.GrevIdPortable,
            DataStrategy: DataStrategy.GrevId,
            Launch: new AppLaunchDefinition(
                Executable: launch.Executable,
                Arguments: $"-batch -fullscreen -bigpicture {quotedGamePath}",
                WorkingDirectory: launch.WorkingDirectory,
                ProcessName: launch.ProcessName,
                SingleInstance: false,
                AdditionalProcessNames: launch.AdditionalProcessNames,
                TrackDescendantProcesses: launch.TrackDescendantProcesses,
                ForceKillEntireProcessTree: launch.ForceKillEntireProcessTree),
            SupportsController: true,
            Description: $"{GameLibraryService.GetPlatformDisplayName(game.Platform)} game launched through PCSX2.");

        var manifest = new InstalledAppManifest(
            definition,
            pcsx2.Manifest.Version,
            game.AddedAtUtc,
            grevId);

        // The game is content owned by this GrevID, not another PCSX2 installation. Runtime launch
        // deliberately borrows the existing PCSX2 binary/data roots so portable.txt continues to
        // point PCSX2 at this same profile's BIOS, memory cards and emulator configuration.
        return new InstalledAppEntry(
            manifest,
            pcsx2.BinaryRoot,
            pcsx2.DataRoot,
            true,
            null);
    }

    private static string QuoteArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";
}
