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
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(path);
            var preference = JsonSerializer.Deserialize<AppControllerGuidePreference>(json, _jsonOptions);
            if (preference is null || preference.Version != CurrentVersion)
            {
                CorruptDataQuarantine.TryPreserve(
                    _paths,
                    path,
                    "AppControllerGuide",
                    "Controller guide preference is empty or uses an unsupported schema version.",
                    out _);
                return true;
            }

            return preference.ShowOnLaunch;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt/unreadable preference data must never hide essential controller help, but it
            // is still user-authored state and must be preserved before a later save replaces it.
            CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "AppControllerGuide",
                $"Controller guide preference could not be read: {ex.Message}",
                out _);
            return true;
        }
    }

    public void DisableForProfile(string grevId, string appId) =>
        WritePreference(grevId, appId, showOnLaunch: false);

    public void ResetForProfile(string grevId, string appId)
    {
        var path = _paths.GetProfileAppControllerGuidePreferenceFile(grevId, appId);
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }

    private void WritePreference(string grevId, string appId, bool showOnLaunch)
    {
        var path = _paths.GetProfileAppControllerGuidePreferenceFile(grevId, appId);
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
