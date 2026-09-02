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
            _ => throw new InvalidOperationException("That game platform is not supported by this Grev Home build.")
        };
    }

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
