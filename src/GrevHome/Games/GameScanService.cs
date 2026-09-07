using System.Text.RegularExpressions;

namespace GrevHome.Games;

/// <summary>How the scanner worked out which console a file belongs to.</summary>
public enum GameScanConfidence
{
    /// <summary>A parent folder was named after the console - the strongest signal.</summary>
    FolderName,

    /// <summary>The file extension is used by exactly one console in this build.</summary>
    Extension,

    /// <summary>Several consoles share this extension. The user picks before it is added.</summary>
    Ambiguous
}

public sealed record GameScanCandidate(
    string SourcePath,
    string SuggestedName,
    string FileTitle,
    GamePlatform? Platform,
    IReadOnlyList<GamePlatform> Alternatives,
    GameScanConfidence Confidence);

public sealed record GameScanReport(
    IReadOnlyList<GameScanCandidate> Candidates,
    int FilesInspected,
    int AlreadyInLibrary,
    bool StoppedAtLimit);

/// <summary>
/// Walks a folder and works out which of its files are games, and which console each one is for.
///
/// Detection is deliberately conservative, because getting the console wrong is worse than not
/// guessing at all: a PS2 .iso mislabelled as GameCube would be routed to a RetroArch core instead
/// of PCSX2 and simply fail to launch. So the scanner never silently guesses between consoles that
/// share an extension - it reports those as Ambiguous and the review screen makes the user choose.
/// </summary>
public sealed class GameScanService
{
    /// <summary>Upper bound on files looked at, so pointing this at a whole drive cannot run forever.</summary>
    public const int MaxFilesInspected = 40_000;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<GamePlatform>> ExtensionMap = BuildExtensionMap();
    private static readonly IReadOnlyDictionary<string, GamePlatform> FolderAliases = BuildFolderAliases();

    // Scanning a drive root should not descend into Windows itself or into an application's own
    // support files. None of these ever hold a user's game collection.
    private static readonly HashSet<string> SkippedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin",
        "System Volume Information", "AppData", "node_modules", "$WinREAgent", "Recovery"
    };

    public Task<GameScanReport> ScanAsync(
        string root,
        IReadOnlyList<GameLibraryEntry> existingGames,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(root, existingGames, cancellationToken), cancellationToken);

    private static GameScanReport Scan(
        string root,
        IReadOnlyList<GameLibraryEntry> existingGames,
        CancellationToken cancellationToken)
    {
        var rootFull = Path.GetFullPath(root);
        if (!Directory.Exists(rootFull))
        {
            throw new DirectoryNotFoundException($"Folder not found: {rootFull}");
        }

        var known = new HashSet<string>(
            existingGames.Select(game => SafeFullPath(game.SourcePath)).Where(path => path.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var candidates = new List<GameScanCandidate>();
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(rootFull);
        var inspected = 0;
        var alreadyInLibrary = 0;
        var stoppedAtLimit = false;

        while (pending.Count > 0 && !stoppedAtLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var canonicalDirectory = SafeFullPath(directory);
            if (canonicalDirectory.Length == 0 || !visited.Add(canonicalDirectory)) continue;

            var referencedTracks = GetReferencedDiscFiles(directory);

            foreach (var file in EnumerateSafely(directory, files: true))
            {
                if (++inspected > MaxFilesInspected)
                {
                    stoppedAtLimit = true;
                    break;
                }

                var extension = Path.GetExtension(file);
                if (string.IsNullOrEmpty(extension) || !ExtensionMap.ContainsKey(extension))
                {
                    continue;
                }

                // CUE and M3U descriptor files are what emulators should launch. Adding every BIN
                // track or every disc listed by an M3U creates duplicate, often unlaunchable tiles.
                if (referencedTracks.Contains(SafeFullPath(file))) continue;

                if (known.Contains(SafeFullPath(file)))
                {
                    alreadyInLibrary++;
                    continue;
                }

                candidates.Add(BuildCandidate(file, extension, rootFull));
            }

            foreach (var child in EnumerateSafely(directory, files: false))
            {
                if (SkippedFolderNames.Contains(Path.GetFileName(child)))
                {
                    continue;
                }
                if (IsReparsePoint(child)) continue;
                pending.Push(child);
            }
        }

        var ordered = candidates
            .OrderBy(candidate => candidate.Platform is null)
            .ThenBy(candidate => candidate.SuggestedName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GameScanReport(ordered, inspected, alreadyInLibrary, stoppedAtLimit);
    }

    private static GameScanCandidate BuildCandidate(string file, string extension, string root)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var name = CleanTitle(stem);
        var matches = ExtensionMap[extension];

        // A folder named after a console beats the extension every time - it is the convention
        // every ROM collection already uses, and it is the only thing that can separate a PS2
        // .iso from a GameCube one.
        var fromFolder = FindPlatformFromFolders(file, root);
        if (fromFolder is { } folderPlatform && matches.Contains(folderPlatform))
        {
            return new GameScanCandidate(file, name, stem, folderPlatform, matches, GameScanConfidence.FolderName);
        }

        if (matches.Count == 1)
        {
            return new GameScanCandidate(file, name, stem, matches[0], matches, GameScanConfidence.Extension);
        }

        return new GameScanCandidate(file, name, stem, null, matches, GameScanConfidence.Ambiguous);
    }

    private static GamePlatform? FindPlatformFromFolders(string file, string root)
    {
        var directory = Path.GetDirectoryName(file);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        while (!string.IsNullOrEmpty(directory))
        {
            var name = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(name) &&
                FolderAliases.TryGetValue(NormalizeAlias(name), out var platform))
            {
                return platform;
            }

            // Stop once the scan root itself has been checked; folders above it are not the
            // user's chosen scope and must not influence detection.
            if (string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !(directory + Path.DirectorySeparatorChar).StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSafely(string directory, bool files)
    {
        try
        {
            return files
                ? Directory.EnumerateFiles(directory).ToArray()
                : Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // One unreadable folder must never abort a scan of everything else.
            return Array.Empty<string>();
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static HashSet<string> GetReferencedDiscFiles(string directory)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in EnumerateSafely(directory, files: true)
                     .Where(path => Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetExtension(path).Equals(".m3u", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (new FileInfo(descriptor).Length > 1024 * 1024) continue;
                foreach (var rawLine in File.ReadLines(descriptor))
                {
                    var line = rawLine.Trim();
                    string? relative = null;
                    if (Path.GetExtension(descriptor).Equals(".cue", StringComparison.OrdinalIgnoreCase) &&
                        line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
                    {
                        var quotedStart = line.IndexOf('"');
                        var quotedEnd = line.LastIndexOf('"');
                        relative = quotedStart >= 0 && quotedEnd > quotedStart
                            ? line[(quotedStart + 1)..quotedEnd]
                            : line[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    }
                    else if (Path.GetExtension(descriptor).Equals(".m3u", StringComparison.OrdinalIgnoreCase) &&
                             line.Length > 0 && !line.StartsWith('#'))
                    {
                        relative = line;
                    }

                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    var full = SafeFullPath(Path.Combine(directory, relative.Trim('"')));
                    if (full.Length > 0) referenced.Add(full);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A malformed descriptor is still shown as a candidate; only duplicate suppression
                // is lost for that one file set.
            }
        }
        return referenced;
    }

    private static readonly Regex TrailingTag = new(@"[\s._-]*[\(\[][^\)\]]*[\)\]]\s*$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Turns a ROM filename into something readable: drops the release tags collections use
    /// (region, revision, dump status) and undoes separator characters used in place of spaces.
    /// </summary>
    public static string CleanTitle(string fileStem)
    {
        var name = fileStem.Trim();

        // "Super.Mario.Bros.3.USA" style names use dots as spaces, but "Super Mario Bros. 3"
        // uses one legitimately. Only treat dots as separators when there are no real spaces.
        if (!name.Contains(' ') && name.Count(character => character == '.') >= 2)
        {
            name = name.Replace('.', ' ');
        }

        name = name.Replace('_', ' ');

        string previous;
        do
        {
            previous = name;
            name = TrailingTag.Replace(name, string.Empty).Trim();
        }
        while (!string.Equals(previous, name, StringComparison.Ordinal) && name.Length > 0);

        name = Whitespace.Replace(name, " ").Trim(' ', '-', '.', ',');

        if (string.IsNullOrWhiteSpace(name))
        {
            name = fileStem.Trim();
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Game";
        }

        return name.Length <= 100 ? name : name[..100].TrimEnd();
    }

    private static string NormalizeAlias(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }
        return new string(buffer[..length]);
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extension to consoles, derived from the same table the manual Add Game flow validates
    /// against, so the scanner can never accept a file the library itself would reject.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<GamePlatform>> BuildExtensionMap()
    {
        var map = new Dictionary<string, List<GamePlatform>>(StringComparer.OrdinalIgnoreCase);
        foreach (var platform in Enum.GetValues<GamePlatform>())
        {
            foreach (var extension in GameLibraryService.GetSupportedExtensions(platform))
            {
                if (!map.TryGetValue(extension, out var list))
                {
                    list = new List<GamePlatform>();
                    map[extension] = list;
                }
                list.Add(platform);
            }
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<GamePlatform>)pair.Value.OrderBy(RankPlatform).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orders the choices offered for a shared extension. PlayStation 2 leads because it is the
    /// one console Grev Home installs a dedicated emulator for, and .iso/.bin/.chd collections
    /// are overwhelmingly PS2 in practice.
    /// </summary>
    private static int RankPlatform(GamePlatform platform) => platform switch
    {
        GamePlatform.PlayStation2 => 0,
        GamePlatform.PlayStation => 1,
        GamePlatform.PlayStationPortable => 2,
        GamePlatform.GameCube => 3,
        GamePlatform.Wii => 4,
        GamePlatform.SegaDreamcast => 5,
        GamePlatform.SegaSaturn => 6,
        GamePlatform.SegaCD => 7,
        GamePlatform.SegaGenesis => 8,
        GamePlatform.NintendoEntertainmentSystem => 9,
        GamePlatform.SuperNintendo => 10,
        GamePlatform.Nintendo64 => 11,
        GamePlatform.Arcade => 12,
        _ => 20
    };

    private static IReadOnlyDictionary<string, GamePlatform> BuildFolderAliases()
    {
        var aliases = new Dictionary<string, GamePlatform>(StringComparer.Ordinal);

        void Add(GamePlatform platform, params string[] names)
        {
            // The enum name and the display name always work, plus the shorthand people
            // actually name their folders.
            foreach (var name in names
                         .Append(platform.ToString())
                         .Append(GameLibraryService.GetPlatformDisplayName(platform)))
            {
                var key = NormalizeAlias(name);
                if (key.Length > 0)
                {
                    aliases[key] = platform;
                }
            }
        }

        Add(GamePlatform.PlayStation2, "ps2", "playstation 2", "sony playstation 2");
        Add(GamePlatform.PlayStation, "ps1", "psx", "psone", "playstation 1", "sony playstation");
        Add(GamePlatform.PlayStationPortable, "psp", "playstation portable");
        Add(GamePlatform.Arcade, "mame", "fbneo", "fba", "neogeo", "neo geo", "arcade roms");
        Add(GamePlatform.Atari2600, "2600", "atari 2600", "vcs");
        Add(GamePlatform.Atari5200, "5200", "atari 5200");
        Add(GamePlatform.Atari7800, "7800", "atari 7800");
        Add(GamePlatform.AtariJaguar, "jaguar", "atari jaguar");
        Add(GamePlatform.AtariLynx, "lynx", "atari lynx");
        Add(GamePlatform.NintendoEntertainmentSystem, "nes", "famicom", "nintendo nes");
        Add(GamePlatform.SuperNintendo, "snes", "sfc", "super famicom", "super nes", "supernintendo");
        Add(GamePlatform.Nintendo64, "n64", "nintendo 64");
        Add(GamePlatform.GameBoy, "gb", "gameboy", "game boy");
        Add(GamePlatform.GameBoyColor, "gbc", "gameboy color", "gameboy colour", "game boy colour");
        Add(GamePlatform.GameBoyAdvance, "gba", "gameboy advance", "game boy advance");
        Add(GamePlatform.NintendoDS, "nds", "ds", "nintendo ds");
        Add(GamePlatform.Nintendo3DS, "3ds", "n3ds", "nintendo 3ds");
        Add(GamePlatform.GameCube, "gc", "ngc", "gamecube", "game cube", "nintendo gamecube");
        Add(GamePlatform.Wii, "wii", "nintendo wii");
        Add(GamePlatform.SegaMasterSystem, "sms", "master system", "sega master system");
        Add(GamePlatform.SegaGenesis, "genesis", "megadrive", "mega drive", "md", "sega genesis", "sega megadrive");
        Add(GamePlatform.SegaGameGear, "gg", "gamegear", "game gear", "sega game gear");
        Add(GamePlatform.SegaCD, "segacd", "sega cd", "megacd", "mega cd");
        Add(GamePlatform.Sega32X, "32x", "sega 32x");
        Add(GamePlatform.SegaSaturn, "saturn", "sega saturn");
        Add(GamePlatform.SegaDreamcast, "dc", "dreamcast", "sega dreamcast");
        Add(GamePlatform.PcEngine, "pce", "pcengine", "pc engine", "turbografx", "turbografx16", "tg16");
        Add(GamePlatform.PcEngineCD, "pcecd", "pc engine cd", "turbografx cd", "tgcd");
        Add(GamePlatform.NeoGeoPocket, "ngp", "ngpc", "neo geo pocket");
        Add(GamePlatform.WonderSwan, "ws", "wsc", "wonderswan", "wonder swan");
        Add(GamePlatform.Commodore64, "c64", "commodore 64");
        Add(GamePlatform.CommodoreAmiga, "amiga", "commodore amiga");

        return aliases;
    }
}
