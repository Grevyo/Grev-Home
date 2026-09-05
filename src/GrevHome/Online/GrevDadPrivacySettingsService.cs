using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, string> _persistenceBlocks =
        new(StringComparer.OrdinalIgnoreCase);

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
            _persistenceBlocks.TryRemove(grevId, out _);
            return GrevDadPrivacySettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<GrevDadPrivacySettings>(stream, _json, cancellationToken);
            if (value is null)
            {
                return RecoverSafeFallback(
                    grevId,
                    path,
                    "Grev.dad privacy settings contained no usable value.");
            }
            if (value.SchemaVersion > SchemaVersion)
            {
                _persistenceBlocks[grevId] =
                    $"Grev.dad privacy settings use schema {value.SchemaVersion}, which is newer than this Grev Home build supports ({SchemaVersion}). Sharing is disabled locally and the existing file will not be overwritten.";
                return GrevDadPrivacySettings.SafeFallback;
            }
            if (value.SchemaVersion != SchemaVersion)
            {
                return RecoverSafeFallback(
                    grevId,
                    path,
                    $"Grev.dad privacy settings use unsupported schema {value.SchemaVersion}.");
            }

            _persistenceBlocks.TryRemove(grevId, out _);
            return NormalizeCurrent(value);
        }
        catch (JsonException ex)
        {
            return RecoverSafeFallback(
                grevId,
                path,
                $"Grev.dad privacy settings could not be parsed: {ex.Message}");
        }
        catch (IOException ex)
        {
            _persistenceBlocks[grevId] =
                $"Grev.dad privacy settings are temporarily unreadable ({ex.Message}). Sharing is disabled locally and the existing file will not be overwritten.";
            return GrevDadPrivacySettings.SafeFallback;
        }
        catch (UnauthorizedAccessException ex)
        {
            _persistenceBlocks[grevId] =
                $"Grev.dad privacy settings cannot currently be accessed ({ex.Message}). Sharing is disabled locally and the existing file will not be overwritten.";
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
            if (_persistenceBlocks.TryGetValue(grevId, out var blockReason) && File.Exists(path))
            {
                throw new InvalidOperationException(blockReason);
            }

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
                _persistenceBlocks.TryRemove(grevId, out _);
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

    private GrevDadPrivacySettings RecoverSafeFallback(
        string grevId,
        string path,
        string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "GrevDadPrivacy",
                reason,
                out _))
        {
            _persistenceBlocks[grevId] =
                "Grev Home found invalid Grev.dad privacy settings but could not preserve a recovery copy. Sharing is disabled locally and the original file will not be overwritten.";
            return GrevDadPrivacySettings.SafeFallback;
        }

        _persistenceBlocks.TryRemove(grevId, out _);
        return GrevDadPrivacySettings.SafeFallback;
    }

    private static GrevDadPrivacySettings NormalizeCurrent(GrevDadPrivacySettings value) =>
        value with
        {
            SchemaVersion = SchemaVersion,
            ActivityVisibility = NormalizeVisibility(value.ActivityVisibility),
            HistoryVisibility = NormalizeVisibility(value.HistoryVisibility)
        };

    private static string NormalizeVisibility(string? value) =>
        string.Equals(value, "friends", StringComparison.OrdinalIgnoreCase)
            ? "friends"
            : "private";

    private string GetSettingsFile(string grevId) =>
        Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad", "privacy.json");
}
