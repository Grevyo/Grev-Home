using System.IO;

namespace GrevHome.Storage;

public sealed record InstallerFirstRunPreferences(
    string GamesRoot,
    string BiosRoot,
    bool EmulatorSetup,
    IReadOnlyList<string> Consoles)
{
    public static InstallerFirstRunPreferences Load(
        AppPaths paths,
        string defaultGamesRoot,
        string defaultBiosRoot)
    {
        var file = Path.Combine(paths.Data, "installer-first-run.ini");
        if (!File.Exists(file))
        {
            return new(defaultGamesRoot, defaultBiosRoot, false, []);
        }

        try
        {
            var values = File.ReadAllLines(file)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var games = values.GetValueOrDefault("GamesRoot");
            var bios = values.GetValueOrDefault("BiosRoot");
            var consoles = (values.GetValueOrDefault("Consoles") ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new(
                string.IsNullOrWhiteSpace(games) ? defaultGamesRoot : games,
                string.IsNullOrWhiteSpace(bios) ? defaultBiosRoot : bios,
                bool.TryParse(values.GetValueOrDefault("EmulatorSetup"), out var enabled) && enabled,
                consoles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(defaultGamesRoot, defaultBiosRoot, false, []);
        }
    }
}
