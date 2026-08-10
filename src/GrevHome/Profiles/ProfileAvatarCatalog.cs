namespace GrevHome.Profiles;

public sealed record ProfileAvatarPreset(string Key, string Glyph, string Name);

public static class ProfileAvatarCatalog
{
    public const string DefaultKey = "initials";

    public static IReadOnlyList<ProfileAvatarPreset> Presets { get; } = new[]
    {
        new ProfileAvatarPreset(DefaultKey, string.Empty, "Initials"),
        new ProfileAvatarPreset("gamepad", "🎮", "Gamepad"),
        new ProfileAvatarPreset("bolt", "⚡", "Bolt"),
        new ProfileAvatarPreset("star", "★", "Star"),
        new ProfileAvatarPreset("diamond", "◆", "Diamond"),
        new ProfileAvatarPreset("shield", "⬢", "Shield")
    };

    public static string Normalize(string? key) =>
        Presets.Any(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase))
            ? Presets.First(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase)).Key
            : DefaultKey;

    public static string GetDisplayGlyph(string? key, string displayName)
    {
        var normalized = Normalize(key);
        var preset = Presets.First(item => item.Key == normalized);
        if (preset.Key != DefaultKey)
        {
            return preset.Glyph;
        }

        var words = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        var first = char.ToUpperInvariant(words[0][0]).ToString();
        if (words.Length == 1)
        {
            return first;
        }

        return first + char.ToUpperInvariant(words[^1][0]);
    }
}
