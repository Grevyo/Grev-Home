using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrevHome.Storage;

namespace GrevHome.Profiles;

public sealed record ProfileAvatarPreset(string Key, string Glyph, string Name);

public static class ProfileAvatarCatalog
{
    public const string DefaultKey = "initials";
    public const string CustomKey = "custom";

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
        string.Equals(key, CustomKey, StringComparison.OrdinalIgnoreCase)
            ? CustomKey
            : Presets.Any(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase))
                ? Presets.First(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase)).Key
                : DefaultKey;

    public static string GetDisplayGlyph(string? key, string displayName)
    {
        var normalized = Normalize(key);
        if (normalized != CustomKey)
        {
            var preset = Presets.First(item => item.Key == normalized);
            if (preset.Key != DefaultKey)
            {
                return preset.Glyph;
            }
        }

        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "?";
        var first = char.ToUpperInvariant(words[0][0]).ToString();
        return words.Length == 1 ? first : first + char.ToUpperInvariant(words[^1][0]);
    }

    public static string? GetCustomImagePath(LocalProfile profile)
    {
        if (!string.Equals(Normalize(profile.AvatarKey), CustomKey, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(profile.AvatarImageFile))
        {
            return null;
        }

        var fileName = Path.GetFileName(profile.AvatarImageFile);
        var path = Path.Combine(new AppPaths().GetProfileRoot(profile.GrevId), fileName);
        return File.Exists(path) ? path : null;
    }

    public static ImageSource? TryLoadCustomImage(LocalProfile profile)
    {
        var path = GetCustomImagePath(profile);
        if (path is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
