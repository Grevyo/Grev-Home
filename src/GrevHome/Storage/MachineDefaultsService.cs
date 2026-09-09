using System.IO;
using System.Text.Json;

namespace GrevHome.Storage;

/// <summary>
/// Machine-wide default locations chosen during first-run setup. Deliberately not per-profile:
/// games and BIOS files are typically large and identical regardless of who's playing, so every
/// profile on this PC points at the same folders here instead of each getting its own copy.
/// The primary Games root is used by Grev-managed installers; AdditionalGamesRoots are library
/// locations Grev Home should remember and surface for scanning/importing content on other drives.
/// Per-profile data (saves, states, screenshots, settings) remains profile-owned.
/// </summary>
public sealed record MachineDefaults(
    int SchemaVersion = 1,
    string? GamesRoot = null,
    string? BiosRoot = null,
    bool SetupCompleted = false,
    IReadOnlyList<string>? AdditionalGamesRoots = null)
{
    public static MachineDefaults NotConfigured { get; } = new();
}

public sealed class MachineDefaultsService
{
    private const int CurrentSchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWeb;

    public MachineDefaultsService(AppPaths paths)
    {
        _paths = paths;
    }

    private string SettingsFile => Path.Combine(_paths.Data, "machine-defaults.json");

    public async Task<MachineDefaults> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFile))
        {
            return MachineDefaults.NotConfigured;
        }

        try
        {
            await using var stream = File.OpenRead(SettingsFile);
            var value = await JsonSerializer.DeserializeAsync<MachineDefaults>(stream, _json, cancellationToken);
            return value is null || value.SchemaVersion != CurrentSchemaVersion
                ? MachineDefaults.NotConfigured
                : value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged settings file must not block startup. First-run setup runs again, which
            // only writes machine-wide folder locations back out.
            return MachineDefaults.NotConfigured;
        }
    }

    /// <summary>Resolves the primary games folder used by Grev-managed installers.</summary>
    public async Task<string> GetGamesRootAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(defaults.GamesRoot) ? DefaultGamesRoot : defaults.GamesRoot;
    }

    /// <summary>Returns every configured Games location, primary first and duplicates removed.</summary>
    public async Task<IReadOnlyList<string>> GetGamesRootsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await GetAsync(cancellationToken);
        var primary = string.IsNullOrWhiteSpace(defaults.GamesRoot) ? DefaultGamesRoot : defaults.GamesRoot;
        return new[] { primary }
            .Concat(defaults.AdditionalGamesRoots ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Resolves the BIOS folder to use right now - configured value or standard default.</summary>
    public async Task<string> GetBiosRootAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(defaults.BiosRoot) ? DefaultBiosRoot : defaults.BiosRoot;
    }

    public string DefaultGamesRoot => Path.Combine(_paths.Root, "Games");
    public string DefaultBiosRoot => Path.Combine(_paths.Root, "Bios");

    // Compatibility overload for existing callers that only know about one Games root.
    public Task SaveAsync(string gamesRoot, string biosRoot, CancellationToken cancellationToken = default) =>
        SaveAsync(gamesRoot, biosRoot, Array.Empty<string>(), cancellationToken);

    public async Task SaveAsync(
        string gamesRoot,
        string biosRoot,
        IReadOnlyList<string> additionalGamesRoots,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gamesRoot))
        {
            throw new ArgumentException("A games folder is required.", nameof(gamesRoot));
        }
        if (string.IsNullOrWhiteSpace(biosRoot))
        {
            throw new ArgumentException("A BIOS folder is required.", nameof(biosRoot));
        }

        var primary = Normalize(gamesRoot);
        var bios = Normalize(biosRoot);
        if (string.Equals(primary, bios, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Games and BIOS folders must be different locations.");
        }

        var additional = (additionalGamesRoots ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Normalize)
            .Where(path => !string.Equals(path, primary, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (additional.Any(path => string.Equals(path, bios, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The BIOS folder cannot also be a Games location.");
        }

        Directory.CreateDirectory(primary);
        foreach (var root in additional) Directory.CreateDirectory(root);
        Directory.CreateDirectory(bios);

        var value = new MachineDefaults(
            CurrentSchemaVersion,
            primary,
            bios,
            SetupCompleted: true,
            AdditionalGamesRoots: additional);

        Directory.CreateDirectory(_paths.Data);
        var temporary = SettingsFile + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, SettingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar);
}
