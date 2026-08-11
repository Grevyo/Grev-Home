using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Input;

public sealed record AppControllerGuidePreference(
    int Version,
    bool ShowOnLaunch);

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
            return preference is null ||
                   preference.Version != CurrentVersion ||
                   preference.ShowOnLaunch;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt/unreadable preference data must never hide essential controller help.
            return true;
        }
    }

    public void DisableForProfile(string grevId, string appId)
    {
        var path = _paths.GetProfileAppControllerGuidePreferenceFile(grevId, appId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var value = new AppControllerGuidePreference(CurrentVersion, ShowOnLaunch: false);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, _jsonOptions));
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
