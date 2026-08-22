using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrevHome.Storage;

namespace GrevHome.Input;

public enum AppControllerControl
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    Menu,
    View,
    LeftThumb,
    RightThumb,
    LeftStick,
    RightStick
}

public enum AppControllerOutputKind
{
    None,
    KeyboardShortcut,
    GrevKeyboard,
    MouseLeftClick,
    MouseRightClick,
    MouseCursor,
    MouseScroll,
    MediaCommand
}

public sealed record AppControllerOutput(
    AppControllerOutputKind Kind,
    string? Value = null);

public sealed record AppControllerMapping(
    AppControllerControl Control,
    AppControllerOutput Output);

public sealed record AppControllerProfileDefaults(
    bool Enabled = false,
    IReadOnlyList<AppControllerMapping>? Mappings = null)
{
    public static AppControllerProfileDefaults Empty { get; } = new();
}

public sealed record AppControllerProfileOverride(
    int Version,
    bool Enabled,
    IReadOnlyList<AppControllerMapping> Mappings);

public sealed record ResolvedAppControllerProfile(
    bool Enabled,
    IReadOnlyList<AppControllerMapping> Mappings,
    bool HasUserOverride);

public sealed record AppControllerOutputPreset(
    string Label,
    AppControllerOutput Output);

public static class AppControllerProfileLayout
{
    public static IReadOnlyList<AppControllerControl> Controls { get; } =
    [
        AppControllerControl.DPadUp,
        AppControllerControl.DPadDown,
        AppControllerControl.DPadLeft,
        AppControllerControl.DPadRight,
        AppControllerControl.A,
        AppControllerControl.B,
        AppControllerControl.X,
        AppControllerControl.Y,
        AppControllerControl.LeftShoulder,
        AppControllerControl.RightShoulder,
        AppControllerControl.LeftTrigger,
        AppControllerControl.RightTrigger,
        AppControllerControl.Menu,
        AppControllerControl.View,
        AppControllerControl.LeftThumb,
        AppControllerControl.RightThumb,
        AppControllerControl.LeftStick,
        AppControllerControl.RightStick
    ];

    public static string FormatControl(AppControllerControl control) => control switch
    {
        AppControllerControl.DPadUp => "D-Pad Up",
        AppControllerControl.DPadDown => "D-Pad Down",
        AppControllerControl.DPadLeft => "D-Pad Left",
        AppControllerControl.DPadRight => "D-Pad Right",
        AppControllerControl.A => "A",
        AppControllerControl.B => "B",
        AppControllerControl.X => "X",
        AppControllerControl.Y => "Y",
        AppControllerControl.LeftShoulder => "LB / Left Shoulder",
        AppControllerControl.RightShoulder => "RB / Right Shoulder",
        AppControllerControl.LeftTrigger => "LT / Left Trigger",
        AppControllerControl.RightTrigger => "RT / Right Trigger",
        AppControllerControl.Menu => "Menu / Start",
        AppControllerControl.View => "View / Back",
        AppControllerControl.LeftThumb => "Left Stick Click / L3",
        AppControllerControl.RightThumb => "Right Stick Click / R3",
        AppControllerControl.LeftStick => "Left Stick",
        AppControllerControl.RightStick => "Right Stick",
        _ => control.ToString()
    };
}

public static class AppControllerOutputCatalog
{
    public static IReadOnlyList<AppControllerOutputPreset> Presets { get; } =
    [
        new("Unassigned", new AppControllerOutput(AppControllerOutputKind.None)),
        new("Select / Enter", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "ENTER")),
        new("Back / Escape", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "ESCAPE")),
        new("Navigate Up", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "UP")),
        new("Navigate Down", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "DOWN")),
        new("Navigate Left", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "LEFT")),
        new("Navigate Right", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "RIGHT")),
        new("Tab Forward", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "TAB")),
        new("Tab Back", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "SHIFT TAB")),
        new("Next Section / F6", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "F6")),
        new("Previous Section / Shift+F6", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "SHIFT F6")),
        new("Previous Item / Alt+Up", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "ALT UP")),
        new("Next Item / Alt+Down", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "ALT DOWN")),
        new("Quick Switcher / Ctrl+K", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "CTRL K")),
        new("Mute / Ctrl+Shift+M", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "CTRL SHIFT M")),
        new("Deafen / Ctrl+Shift+D", new AppControllerOutput(AppControllerOutputKind.KeyboardShortcut, "CTRL SHIFT D")),
        new("Grev On-Screen Keyboard", new AppControllerOutput(AppControllerOutputKind.GrevKeyboard)),
        new("Mouse Left Click", new AppControllerOutput(AppControllerOutputKind.MouseLeftClick)),
        new("Mouse Right Click", new AppControllerOutput(AppControllerOutputKind.MouseRightClick)),
        new("Mouse Cursor", new AppControllerOutput(AppControllerOutputKind.MouseCursor)),
        new("Mouse Scroll", new AppControllerOutput(AppControllerOutputKind.MouseScroll)),
        new("Media Play / Pause", new AppControllerOutput(AppControllerOutputKind.MediaCommand, "PLAY_PAUSE")),
        new("Media Previous", new AppControllerOutput(AppControllerOutputKind.MediaCommand, "PREVIOUS")),
        new("Media Next", new AppControllerOutput(AppControllerOutputKind.MediaCommand, "NEXT"))
    ];

    public static string Format(AppControllerOutput output)
    {
        var match = Presets.FirstOrDefault(preset => preset.Output == output);
        return match?.Label ?? output.Kind switch
        {
            AppControllerOutputKind.KeyboardShortcut when !string.IsNullOrWhiteSpace(output.Value) => output.Value,
            AppControllerOutputKind.None => "Unassigned",
            _ => output.Kind.ToString()
        };
    }

    public static AppControllerOutput Move(AppControllerOutput current, int delta)
    {
        var index = Presets.ToList().FindIndex(preset => preset.Output == current);
        if (index < 0) index = 0;
        var count = Presets.Count;
        var next = ((index + delta) % count + count) % count;
        return Presets[next].Output;
    }
}

public sealed class AppControllerProfileService
{
    private const int CurrentVersion = 1;
    private const int MaximumOutputValueLength = 80;

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public AppControllerProfileService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ResolvedAppControllerProfile> ResolveAsync(
        string grevId,
        string appId,
        AppControllerProfileDefaults? defaults,
        CancellationToken cancellationToken = default)
    {
        var userOverride = await ReadOverrideAsync(grevId, appId, cancellationToken);
        return userOverride is null
            ? ResolveDefaults(defaults)
            : new ResolvedAppControllerProfile(
                userOverride.Enabled,
                NormalizeMappings(userOverride.Mappings),
                HasUserOverride: true);
    }

    public static ResolvedAppControllerProfile ResolveDefaults(AppControllerProfileDefaults? defaults)
    {
        defaults ??= AppControllerProfileDefaults.Empty;
        return new ResolvedAppControllerProfile(
            defaults.Enabled,
            NormalizeMappings(defaults.Mappings),
            HasUserOverride: false);
    }

    public async Task SaveAsync(
        string grevId,
        string appId,
        bool enabled,
        IReadOnlyList<AppControllerMapping> mappings,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeMappings(mappings);
        var value = new AppControllerProfileOverride(CurrentVersion, enabled, normalized);
        var path = _paths.GetProfileAppControllerProfileFile(grevId, appId);
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
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
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

    public Task ResetAsync(
        string grevId,
        string appId,
        CancellationToken cancellationToken = default)
    {
        var path = _paths.GetProfileAppControllerProfileFile(grevId, appId);
        return Task.Run(() =>
        {
            if (File.Exists(path)) File.Delete(path);
            var root = Path.GetDirectoryName(path);
            if (root is not null && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root);
            }
        }, cancellationToken);
    }

    private async Task<AppControllerProfileOverride?> ReadOverrideAsync(
        string grevId,
        string appId,
        CancellationToken cancellationToken)
    {
        var path = _paths.GetProfileAppControllerProfileFile(grevId, appId);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<AppControllerProfileOverride>(stream, _jsonOptions, cancellationToken);
            if (value is null || value.Version != CurrentVersion)
            {
                CorruptDataQuarantine.TryPreserve(
                    _paths,
                    path,
                    "AppControllerProfile",
                    "Controller profile is empty or uses an unsupported schema version.",
                    out _);
                return null;
            }
            return value with { Mappings = NormalizeMappings(value.Mappings) };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "AppControllerProfile",
                $"Controller profile could not be read or validated: {ex.Message}",
                out _);
            return null;
        }
    }

    private static IReadOnlyList<AppControllerMapping> NormalizeMappings(IReadOnlyList<AppControllerMapping>? mappings)
    {
        var source = mappings ?? Array.Empty<AppControllerMapping>();
        var lookup = new Dictionary<AppControllerControl, AppControllerOutput>();

        foreach (var mapping in source)
        {
            if (!Enum.IsDefined(mapping.Control))
            {
                throw new InvalidOperationException("Controller profile contains an unknown controller control.");
            }

            if (lookup.ContainsKey(mapping.Control))
            {
                throw new InvalidOperationException("Controller profile contains the same controller control more than once.");
            }

            lookup[mapping.Control] = NormalizeOutput(mapping.Output);
        }

        return AppControllerProfileLayout.Controls
            .Select(control => new AppControllerMapping(
                control,
                lookup.TryGetValue(control, out var output)
                    ? output
                    : new AppControllerOutput(AppControllerOutputKind.None)))
            .ToArray();
    }

    private static AppControllerOutput NormalizeOutput(AppControllerOutput? output)
    {
        output ??= new AppControllerOutput(AppControllerOutputKind.None);
        if (!Enum.IsDefined(output.Kind))
        {
            throw new InvalidOperationException("Controller profile contains an unknown output type.");
        }

        var value = string.IsNullOrWhiteSpace(output.Value) ? null : output.Value.Trim();
        if (value is { Length: > MaximumOutputValueLength } || value?.Any(char.IsControl) == true)
        {
            throw new InvalidOperationException("Controller profile output values must be 80 characters or fewer and cannot contain control characters.");
        }

        return output with { Value = value };
    }
}
