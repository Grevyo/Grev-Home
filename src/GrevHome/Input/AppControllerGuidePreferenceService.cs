using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Input;

public sealed record AppControllerGuidePreference(
    int Version,
    bool ShowOnLaunch);

/// <summary>
/// Per-GrevID preference for reusable app onboarding/controller guides. The historical class name
/// is retained because the same storage contract now backs generic package onboarding.
/// </summary>
public sealed class AppControllerGuidePreferenceService
{
    private const int CurrentVersion = 1;

    private readonly AppPaths _paths;
    private readonly ConcurrentDictionary<string, string> _persistenceBlocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public AppControllerGuidePreferenceService(AppPaths paths)
    {
        _paths = paths;
    }

    public bool ShouldShow(string? grevId, string appId)
    {
        if (string.IsNullOrWhiteSpace(grevId))
        {
            // Guest/non-persistent sessions can use the guide, but cannot permanently dismiss it.
            return true;
        }

        var path = _paths.GetProfileAppControllerGuidePreferenceFile(grevId, appId);
        var key = BuildPersistenceKey(grevId, appId);
        if (!File.Exists(path))
        {
            _persistenceBlocks.TryRemove(key, out _);
            return true;
        }

        try
        {
            var json = File.ReadAllText(path);
            var preference = JsonSerializer.Deserialize<AppControllerGuidePreference>(json, _jsonOptions);
            if (preference is null)
            {
                return RecoverVisibleFallback(
                    key,
                    path,
                    "Controller guide preference contained no usable value.");
            }
            if (preference.Version > CurrentVersion)
            {
                _persistenceBlocks[key] =
                    $"Controller guide preference uses schema {preference.Version}, which is newer than this Grev Home build supports ({CurrentVersion}). The guide will be shown, but the existing preference will not be overwritten.";
                return true;
            }
            if (preference.Version != CurrentVersion)
            {
                return RecoverVisibleFallback(
                    key,
                    path,
                    $"Controller guide preference uses unsupported schema {preference.Version}.");
            }

            _persistenceBlocks.TryRemove(key, out _);
            return preference.ShowOnLaunch;
        }
        catch (JsonException ex)
        {
            return RecoverVisibleFallback(
                key,
                path,
                $"Controller guide preference could not be parsed: {ex.Message}");
        }
        catch (IOException ex)
        {
            _persistenceBlocks[key] =
                $"Controller guide preference is temporarily unreadable ({ex.Message}). The guide will be shown and the existing preference will not be overwritten.";
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _persistenceBlocks[key] =
                $"Controller guide preference cannot currently be accessed ({ex.Message}). The guide will be shown and the existing preference will not be overwritten.";
            return true;
        }
    }

    public void DisableForProfile(string grevId, string appId) =>
        WritePreference(grevId, appId, showOnLaunch: false);

    public void ResetForProfile(string grevId, string appId)
    {
        var path = _paths.GetProfileAppControllerGuidePreferenceFile(grevId, appId);
        var key = BuildPersistenceKey(grevId, appId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        _persistenceBlocks.TryRemove(key, out _);
    }

    private bool RecoverVisibleFallback(string key, string path, string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "AppControllerGuide",
                reason,
                out _))
        {
            _persistenceBlocks[key] =
                "Grev Home found invalid controller-guide preference data but could not preserve a recovery copy. The guide will be shown and the original file will not be overwritten.";
            return true;
        }

        _persistenceBlocks.TryRemove(key, out _);
        return true;
    }

    private void WritePreference(string grevId, string appId, bool showOnLaunch)
    {
        var path = _paths.GetProfileAppControllerGuidePreferenceFile(grevId, appId);
        var key = BuildPersistenceKey(grevId, appId);
        if (_persistenceBlocks.TryGetValue(key, out var blockReason) && File.Exists(path))
        {
            throw new InvalidOperationException(blockReason);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var value = new AppControllerGuidePreference(CurrentVersion, showOnLaunch);
        var temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(value, _jsonOptions));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            _persistenceBlocks.TryRemove(key, out _);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string BuildPersistenceKey(string grevId, string appId) =>
        $"{grevId}\u001f{appId}";
}
