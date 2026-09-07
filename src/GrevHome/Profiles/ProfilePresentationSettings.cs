using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrevHome.Storage;

namespace GrevHome.Profiles;

public enum ProfileShowcaseMode
{
    TopPlayed,
    RecentActivity,
    Milestones
}

public enum ProfileCardFrame { Role, Clean, Glow, Double }
public enum ProfileAvatarShape { Circle, Rounded, Square }

public sealed record ProfilePresentationSettings(
    int SchemaVersion = 1,
    string BannerKey = ProfileBannerCatalog.DefaultKey,
    string? BannerImageFile = null,
    ProfileShowcaseMode ShowcaseMode = ProfileShowcaseMode.TopPlayed,
    ProfileCardFrame CardFrame = ProfileCardFrame.Role,
    ProfileAvatarShape AvatarShape = ProfileAvatarShape.Circle,
    bool ShowUsername = true,
    bool ShowLevel = true,
    bool ShowXp = true,
    bool ShowPlaytime = true,
    bool ShowSessions = true,
    bool ShowStatus = true)
{
    public static ProfilePresentationSettings Default { get; } = new();
}

public sealed record ProfileBannerPreset(
    string Key,
    string Name,
    string StartColor,
    string MiddleColor,
    string EndColor);

public static class ProfileBannerCatalog
{
    public const string DefaultKey = "grev";
    public const string CustomKey = "custom";

    public static IReadOnlyList<ProfileBannerPreset> Presets { get; } = new[]
    {
        new ProfileBannerPreset(DefaultKey, "Grev", "#243451", "#171D2B", "#0A0E15"),
        new ProfileBannerPreset("midnight", "Midnight", "#10182A", "#0C1220", "#06090F"),
        new ProfileBannerPreset("ember", "Ember", "#4C2018", "#251513", "#090C12"),
        new ProfileBannerPreset("aurora", "Aurora", "#153D42", "#172A38", "#090C12"),
        new ProfileBannerPreset("violet", "Violet", "#362451", "#21182F", "#090C12"),
        new ProfileBannerPreset("mono", "Mono", "#343A44", "#1D222A", "#090C12")
    };

    public static string Normalize(string? key)
    {
        if (string.Equals(key, CustomKey, StringComparison.OrdinalIgnoreCase)) return CustomKey;
        return Presets.FirstOrDefault(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase))?.Key
               ?? DefaultKey;
    }

    public static LinearGradientBrush CreateBrush(string? key)
    {
        var normalized = Normalize(key);
        var preset = Presets.FirstOrDefault(item => string.Equals(item.Key, normalized, StringComparison.OrdinalIgnoreCase))
                     ?? Presets[0];
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(preset.StartColor), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(preset.MiddleColor), 0.48));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(preset.EndColor), 1));
        brush.Freeze();
        return brush;
    }

    public static ImageSource? TryLoadCustomImage(string grevId, ProfilePresentationSettings settings)
    {
        if (!string.Equals(Normalize(settings.BannerKey), CustomKey, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.BannerImageFile))
        {
            return null;
        }

        var fileName = Path.GetFileName(settings.BannerImageFile);
        var path = Path.Combine(new AppPaths().GetProfileRoot(grevId), fileName);
        if (!File.Exists(path)) return null;

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

/// <summary>
/// Single source of truth for turning an avatar shape choice into actual rendering. Shared by
/// every place an avatar is rendered - your own profile, the friend card, and the friend profile
/// view - so "Avatar Shape" in Edit Profile has one consistent visual meaning wherever an avatar
/// is drawn, local or a friend's synced card.
/// </summary>
public static class ProfileAvatarShapeStyle
{
    public static double GetRadius(ProfileAvatarShape shape, double diameter) => shape switch
    {
        ProfileAvatarShape.Circle => diameter / 2,
        ProfileAvatarShape.Rounded => Math.Min(20, diameter / 4),
        _ => 0
    };

    /// <summary>Overload for GrevDadPublicCard.AvatarShape, which travels as a lowercase string over the wire.</summary>
    public static double GetRadius(string? shapeName, double diameter) =>
        GetRadius(Enum.TryParse<ProfileAvatarShape>(shapeName, ignoreCase: true, out var shape) ? shape : ProfileAvatarShape.Circle, diameter);

    /// <summary>
    /// Applies the shape to a Border's own rounded rendering AND clips its content to match.
    /// Border.CornerRadius only rounds the border's own background/stroke - it does not clip
    /// child content, so without an explicit Clip geometry a "Circle" avatar still shows a
    /// square image with only the very corners of the border itself rounded.
    /// </summary>
    public static void Apply(Border border, ProfileAvatarShape shape, double diameter)
    {
        var radius = GetRadius(shape, diameter);
        border.CornerRadius = new CornerRadius(radius);
        border.Clip = new RectangleGeometry(new Rect(0, 0, diameter, diameter), radius, radius);
    }

    public static void Apply(Border border, string? shapeName, double diameter) =>
        Apply(border, Enum.TryParse<ProfileAvatarShape>(shapeName, ignoreCase: true, out var shape) ? shape : ProfileAvatarShape.Circle, diameter);
}

public sealed class ProfilePresentationSettingsService
{
    private const int CurrentSchemaVersion = 2;
    private const long MaxBannerFileBytes = 15 * 1024 * 1024;
    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp" };

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWithStringEnums;

    public ProfilePresentationSettingsService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ProfilePresentationSettings> GetAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var path = GetSettingsFile(grevId);
        if (!File.Exists(path)) return ProfilePresentationSettings.Default;

        try
        {
            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<ProfilePresentationSettings>(stream, _json, cancellationToken);
            if (settings is null)
            {
                return RecoverDefaults(path, "Profile presentation settings contained no usable value.");
            }
            if (settings.SchemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Profile presentation schema {settings.SchemaVersion} is newer than this Grev Home build supports ({CurrentSchemaVersion}).");
            }
            if (settings.SchemaVersion < 1 || !Enum.IsDefined(settings.ShowcaseMode) ||
                !Enum.IsDefined(settings.CardFrame) || !Enum.IsDefined(settings.AvatarShape))
            {
                return RecoverDefaults(path, "Profile presentation settings used an unsupported schema or showcase mode.");
            }

            return settings with { SchemaVersion = CurrentSchemaVersion, BannerKey = ProfileBannerCatalog.Normalize(settings.BannerKey) };
        }
        catch (JsonException ex)
        {
            return RecoverDefaults(path, $"Profile presentation JSON could not be parsed: {ex.Message}");
        }
    }

    public async Task<ProfilePresentationSettings> SaveAsync(
        string grevId,
        string bannerKey,
        ProfileShowcaseMode showcaseMode,
        string? customBannerSourcePath = null,
        ProfileCardFrame cardFrame = ProfileCardFrame.Role,
        ProfileAvatarShape avatarShape = ProfileAvatarShape.Circle,
        bool showUsername = true,
        bool showLevel = true,
        bool showXp = true,
        bool showPlaytime = true,
        bool showSessions = true,
        bool showStatus = true,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(showcaseMode))
        {
            throw new InvalidOperationException("Choose a valid profile showcase mode.");
        }

        var existing = await GetAsync(grevId, cancellationToken);
        var normalizedBanner = ProfileBannerCatalog.Normalize(bannerKey);
        var previousBannerFile = existing.BannerImageFile;
        string? bannerImageFile = existing.BannerImageFile;

        if (string.Equals(normalizedBanner, ProfileBannerCatalog.CustomKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(customBannerSourcePath))
            {
                bannerImageFile = await ImportBannerImageAsync(grevId, customBannerSourcePath, cancellationToken);
            }
            else if (string.IsNullOrWhiteSpace(bannerImageFile) ||
                     !File.Exists(Path.Combine(_paths.GetProfileRoot(grevId), Path.GetFileName(bannerImageFile))))
            {
                throw new InvalidOperationException("Choose a custom profile banner first.");
            }
        }
        else
        {
            bannerImageFile = null;
        }

        var updated = new ProfilePresentationSettings(
            CurrentSchemaVersion,
            normalizedBanner,
            bannerImageFile,
            showcaseMode,
            cardFrame,
            avatarShape,
            showUsername,
            showLevel,
            showXp,
            showPlaytime,
            showSessions,
            showStatus);
        await WriteSettingsAsync(grevId, updated, cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousBannerFile) &&
            !string.Equals(previousBannerFile, bannerImageFile, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteBannerFile(grevId, previousBannerFile);
        }

        return updated;
    }

    private async Task<string> ImportBannerImageAsync(
        string grevId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("That profile banner no longer exists.", source);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!SupportedImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Profile banners must be PNG, JPG, JPEG or BMP images.");
        }

        var info = new FileInfo(source);
        if (info.Length <= 0 || info.Length > MaxBannerFileBytes)
        {
            throw new InvalidOperationException("Profile banners must be larger than 0 bytes and no more than 15 MB.");
        }

        try
        {
            using var validationStream = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(validationStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidOperationException("That file does not contain a readable image.");
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            throw new InvalidOperationException("That file is not a readable profile banner.", ex);
        }

        var profileRoot = _paths.GetProfileRoot(grevId);
        Directory.CreateDirectory(profileRoot);
        var fileName = $"banner{extension}";
        var target = Path.Combine(profileRoot, fileName);
        var temporary = Path.Combine(profileRoot, $"banner-upload-{Guid.NewGuid():N}{extension}.tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return fileName;
    }

    private async Task WriteSettingsAsync(
        string grevId,
        ProfilePresentationSettings settings,
        CancellationToken cancellationToken)
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
                await JsonSerializer.SerializeAsync(stream, settings, _json, cancellationToken);
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

    private ProfilePresentationSettings RecoverDefaults(string path, string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, path, "ProfilePresentation", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found invalid profile presentation data but could not preserve a recovery copy. The original file was left untouched.");
        }
        return ProfilePresentationSettings.Default;
    }

    private string GetSettingsFile(string grevId) =>
        Path.Combine(_paths.GetProfileRoot(grevId), "presentation.json");

    private void TryDeleteBannerFile(string grevId, string bannerFile)
    {
        try
        {
            var fileName = Path.GetFileName(bannerFile);
            if (!fileName.StartsWith("banner", StringComparison.OrdinalIgnoreCase)) return;
            var path = Path.Combine(_paths.GetProfileRoot(grevId), fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A stale old banner is harmless and must not fail an otherwise successful presentation save.
        }
    }
}
