using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Online;

public sealed record GrevDadPrivacySettings(
    int SchemaVersion,
    bool SharePresence,
    bool SharePlayingStatus,
    bool ShareLiveActivityEvents,
    bool ShareSessionHistory,
    string ActivityVisibility,
    string HistoryVisibility)
{
    public static GrevDadPrivacySettings Default { get; } = new(
        SchemaVersion: 1,
        SharePresence: true,
        SharePlayingStatus: true,
        ShareLiveActivityEvents: true,
        ShareSessionHistory: true,
        ActivityVisibility: "friends",
        HistoryVisibility: "friends");

    public static GrevDadPrivacySettings SafeFallback { get; } = new(
        SchemaVersion: 1,
        SharePresence: false,
        SharePlayingStatus: false,
        ShareLiveActivityEvents: false,
        ShareSessionHistory: false,
        ActivityVisibility: "private",
        HistoryVisibility: "private");
}

/// <summary>
/// Local GrevID-owned privacy policy for the optional Grev.dad bridge. These settings never affect
/// local Grev Home functionality; they only decide which online presence/activity/history data is
/// published when a GrevID happens to be linked.
/// </summary>
public sealed class GrevDadPrivacySettingsService
{
    private const int SchemaVersion = 1;
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GrevDadPrivacySettingsService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<GrevDadPrivacySettings> GetAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureProfileLayout(grevId);
        var path = GetSettingsFile(grevId);
        if (!File.Exists(path))
        {
            return GrevDadPrivacySettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<GrevDadPrivacySettings>(stream, _json, cancellationToken);
            if (value is null || value.SchemaVersion != SchemaVersion)
            {
                CorruptDataQuarantine.TryPreserve(
                    _paths,
                    path,
                    "GrevDadPrivacy",
                    "Grev.dad privacy settings are empty or use an unsupported schema version.",
                    out _);
                return GrevDadPrivacySettings.SafeFallback;
            }

            return NormalizeCurrent(value);
        }
        catch (JsonException ex)
        {
            CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "GrevDadPrivacy",
                $"Grev.dad privacy settings could not be parsed: {ex.Message}",
                out _);
            return GrevDadPrivacySettings.SafeFallback;
        }
        catch (IOException)
        {
            return GrevDadPrivacySettings.SafeFallback;
        }
        catch (UnauthorizedAccessException)
        {
            return GrevDadPrivacySettings.SafeFallback;
        }
    }

    public async Task<GrevDadPrivacySettings> SaveAsync(
        string grevId,
        GrevDadPrivacySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureProfileLayout(grevId);
        var normalized = NormalizeCurrent(settings with { SchemaVersion = SchemaVersion });

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetSettingsFile(grevId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
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
                    await JsonSerializer.SerializeAsync(stream, normalized, _json, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }

        return normalized;
    }

    private static GrevDadPrivacySettings NormalizeCurrent(GrevDadPrivacySettings value) =>
        value with
        {
            SchemaVersion = SchemaVersion,
            ActivityVisibility = NormalizeVisibility(value.ActivityVisibility),
            HistoryVisibility = NormalizeVisibility(value.HistoryVisibility)
        };

    private static string NormalizeVisibility(string? value) =>
        string.Equals(value, "private", StringComparison.OrdinalIgnoreCase)
            ? "private"
            : "friends";

    private string GetSettingsFile(string grevId) =>
        Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad", "privacy.json");
}
