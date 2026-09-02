using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrevHome.Storage;

namespace GrevHome.Games;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GamePlatform
{
    PlayStation2
}

public sealed record GameLibraryEntry(
    string GameId,
    string DisplayName,
    GamePlatform Platform,
    string SourcePath,
    DateTimeOffset AddedAtUtc);

internal sealed record GameLibraryDocument(
    int SchemaVersion,
    IReadOnlyList<GameLibraryEntry> Games);

public sealed class GameLibraryService
{
    private const int SchemaVersion = 1;
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GameLibraryService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<GameLibraryEntry>> GetForProfileAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        var path = GetLibraryFile(grevId);
        if (!File.Exists(path))
        {
            return Array.Empty<GameLibraryEntry>();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<GameLibraryDocument>(stream, _json, cancellationToken);
            if (document is null)
            {
                return RecoverOrThrow(path, "Game library contained no usable value.");
            }
            if (document.SchemaVersion > SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Game library schema {document.SchemaVersion} is newer than this Grev Home build supports ({SchemaVersion}). The existing library was left untouched.");
            }
            if (document.SchemaVersion != SchemaVersion || document.Games is null)
            {
                return RecoverOrThrow(path, $"Game library used unsupported schema {document.SchemaVersion}.");
            }

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<GameLibraryEntry>();
            foreach (var game in document.Games)
            {
                if (!IsValid(game) || !seenIds.Add(game.GameId))
                {
                    return RecoverOrThrow(path, "Game library contained invalid or duplicate entries.");
                }
                results.Add(game with { SourcePath = Path.GetFullPath(game.SourcePath) });
            }

            return results
                .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException ex)
        {
            return RecoverOrThrow(path, $"Game library JSON could not be parsed: {ex.Message}");
        }
    }

    public async Task<GameLibraryEntry> AddAsync(
        string grevId,
        GamePlatform platform,
        string sourcePath,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureProfileLayout(grevId);
        var fullPath = ValidateGamePath(platform, sourcePath);
        var games = (await GetForProfileAsync(grevId, cancellationToken)).ToList();

        var existing = games.FirstOrDefault(game => PathsEqual(game.SourcePath, fullPath));
        if (existing is not null)
        {
            return existing;
        }

        var name = NormalizeDisplayName(displayName, fullPath);
        var prefix = platform switch
        {
            GamePlatform.PlayStation2 => "game.ps2",
            _ => "game"
        };
        var entry = new GameLibraryEntry(
            $"{prefix}.{Guid.NewGuid():N}",
            name,
            platform,
            fullPath,
            DateTimeOffset.UtcNow);

        games.Add(entry);
        await WriteAsync(grevId, games, cancellationToken);
        return entry;
    }

    public async Task RemoveAsync(
        string grevId,
        string gameId,
        CancellationToken cancellationToken = default)
    {
        var games = (await GetForProfileAsync(grevId, cancellationToken)).ToList();
        var removed = games.RemoveAll(game => string.Equals(game.GameId, gameId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }
        await WriteAsync(grevId, games, cancellationToken);
    }

    public static string GetPlatformDisplayName(GamePlatform platform) => platform switch
    {
        GamePlatform.PlayStation2 => "PlayStation 2",
        _ => platform.ToString()
    };

    public static IReadOnlySet<string> GetSupportedExtensions(GamePlatform platform) => platform switch
    {
        GamePlatform.PlayStation2 => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".iso", ".chd", ".bin", ".img", ".cso", ".zso", ".gz", ".mdf", ".nrg", ".isz"
        },
        _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    };

    public static bool IsSourceAvailable(GameLibraryEntry game) =>
        !string.IsNullOrWhiteSpace(game.SourcePath) && File.Exists(game.SourcePath);

    private async Task WriteAsync(
        string grevId,
        IReadOnlyList<GameLibraryEntry> games,
        CancellationToken cancellationToken)
    {
        var path = GetLibraryFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var document = new GameLibraryDocument(SchemaVersion, games);

        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private IReadOnlyList<GameLibraryEntry> RecoverOrThrow(string path, string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, path, "GameLibrary", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found an invalid game library but could not preserve a recovery copy. The library was left untouched.");
        }
        return Array.Empty<GameLibraryEntry>();
    }

    private string GetLibraryFile(string grevId) =>
        Path.Combine(_paths.GetProfileRoot(grevId), "Library", "games.json");

    private static string ValidateGamePath(GamePlatform platform, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Choose a game file first.", nameof(sourcePath));
        }

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected game file no longer exists.", fullPath);
        }

        var extension = Path.GetExtension(fullPath);
        if (!GetSupportedExtensions(platform).Contains(extension))
        {
            throw new InvalidDataException(
                $"{GetPlatformDisplayName(platform)} does not accept '{extension}' files in this Grev Home build.");
        }
        return fullPath;
    }

    private static string NormalizeDisplayName(string? displayName, string fullPath)
    {
        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : displayName.Trim();
        name = name.Replace('_', ' ').Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "PlayStation 2 Game";
        }
        return name.Length <= 100 ? name : name[..100].TrimEnd();
    }

    private static bool IsValid(GameLibraryEntry game)
    {
        if (game is null || string.IsNullOrWhiteSpace(game.GameId) || game.GameId.Length > 80 ||
            game.GameId.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !(character is >= '0' and <= '9') &&
                character is not '.' and not '-') ||
            string.IsNullOrWhiteSpace(game.DisplayName) || game.DisplayName.Length > 100 ||
            string.IsNullOrWhiteSpace(game.SourcePath) || !Path.IsPathRooted(game.SourcePath) ||
            game.AddedAtUtc == default)
        {
            return false;
        }

        return GetSupportedExtensions(game.Platform).Contains(Path.GetExtension(game.SourcePath));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
