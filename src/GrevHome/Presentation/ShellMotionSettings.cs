using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Presentation;

public sealed record ShellMotionSettings(
    bool ScreenTransitionsEnabled = true,
    bool StartupIntroEnabled = true,
    bool OverlayTransitionsEnabled = true,
    bool ReturnHomeTransitionEnabled = true,
    bool TileFocusAnimationEnabled = true,
    bool ModalTransitionsEnabled = true,
    bool AmbientBackgroundEnabled = true,
    bool DashboardBackgroundsEnabled = true,
    bool ButtonPressFeedbackEnabled = true,
    bool UiSoundsEnabled = true,
    bool StartupSoundEnabled = true,
    bool ControllerVibrationEnabled = true,
    int UiSoundVolumePercent = 50,
    ShellAnimationSpeed AnimationSpeed = ShellAnimationSpeed.Normal,
    ShellVibrationStrength VibrationStrength = ShellVibrationStrength.Low);

public enum ShellAnimationSpeed
{
    Relaxed,
    Normal,
    Fast
}

public enum ShellVibrationStrength
{
    Low,
    Medium,
    High
}

public sealed class ShellMotionSettingsService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ShellMotionSettingsService(AppPaths paths) => _paths = paths;

    public ShellMotionSettings Load()
    {
        try
        {
            if (!File.Exists(_paths.ShellMotionSettingsFile)) return new ShellMotionSettings();
            using var stream = File.OpenRead(_paths.ShellMotionSettingsFile);
            return JsonSerializer.Deserialize<ShellMotionSettings>(stream, _json) ?? new ShellMotionSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ShellMotionSettings();
        }
    }

    public async Task SaveAsync(ShellMotionSettings settings, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.PresentationData);
            var target = _paths.ShellMotionSettingsFile;
            var temporary = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, _json, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
