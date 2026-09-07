using System.IO;
using System.Text.Json;

namespace GrevHome.Storage;

/// <summary>
/// Machine-wide default locations chosen during first-run setup. Deliberately not per-profile:
/// games and BIOS files are typically large and identical regardless of who's playing, so every
/// profile on this PC points at the same folders here instead of each getting its own copy.
/// Per-profile data (saves, states, screenshots, settings) is untouched by this and stays under
/// each profile's own folder as it always has.
/// </summary>
public sealed record MachineDefaults(
    int SchemaVersion = 1,
    string? GamesRoot = null,
    string? BiosRoot = null,
    bool SetupCompleted = false)
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
            // is always safe - it only writes the two folder paths back out.
            return MachineDefaults.NotConfigured;
        }
    }

    /// <summary>Resolves the games folder to use right now - the configured value, or the standard default if setup hasn't run yet.</summary>
    public async Task<string> GetGamesRootAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(defaults.GamesRoot) ? DefaultGamesRoot : defaults.GamesRoot;
    }

    /// <summary>Resolves the BIOS folder to use right now - the configured value, or the standard default if setup hasn't run yet.</summary>
    public async Task<string> GetBiosRootAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(defaults.BiosRoot) ? DefaultBiosRoot : defaults.BiosRoot;
    }

    public string DefaultGamesRoot => Path.Combine(_paths.Root, "Games");
    public string DefaultBiosRoot => Path.Combine(_paths.Root, "Bios");

    public async Task SaveAsync(string gamesRoot, string biosRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gamesRoot))
        {
            throw new ArgumentException("A games folder is required.", nameof(gamesRoot));
        }
        if (string.IsNullOrWhiteSpace(biosRoot))
        {
            throw new ArgumentException("A BIOS folder is required.", nameof(biosRoot));
        }

        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(biosRoot);

        var value = new MachineDefaults(CurrentSchemaVersion, gamesRoot, biosRoot, SetupCompleted: true);
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
}
