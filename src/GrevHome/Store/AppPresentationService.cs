using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store;

public enum AppVisualAssetSlot
{
    Icon,
    TileMedia,
    HeroMedia
}

public sealed record AppPresentationOverride(
    string? DisplayName = null,
    string? TileColor = null,
    string? IconFile = null,
    string? TileMediaFile = null,
    string? HeroMediaFile = null);

public sealed record ResolvedAppPresentation(
    string DisplayName,
    string TileColor,
    string? IconPath,
    string? TileMediaPath,
    string? HeroMediaPath,
    bool HasUserOverrides);

/// <summary>
/// Resolves the locked presentation contract: package defaults first, then per-GrevID overrides.
/// Presentation is independent from installer/runtime state and Reset always reveals the package
/// defaults again.
/// </summary>
public sealed class AppPresentationService
{
    public const long MaxVisualAssetBytes = 25L * 1024L * 1024L;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public AppPresentationService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ResolvedAppPresentation> ResolveAsync(
        string grevId,
        GrevStorePackageDefinition package,
        CancellationToken cancellationToken = default)
    {
        var overrides = await ReadOverrideAsync(grevId, package.App.AppId, cancellationToken);
        return Resolve(package, grevId, overrides);
    }

    public async Task SaveDisplayNameAsync(
        string grevId,
        string appId,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadOverrideAsync(grevId, appId, cancellationToken);
        var normalized = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (normalized is { Length: > 100 })
        {
            throw new InvalidOperationException("Custom app names must be 100 characters or fewer.");
        }

        await WriteOverrideAsync(grevId, appId, current with { DisplayName = normalized }, cancellationToken);
    }

    public async Task SaveTileColorAsync(
        string grevId,
        string appId,
        string? tileColor,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadOverrideAsync(grevId, appId, cancellationToken);
        await WriteOverrideAsync(
            grevId,
            appId,
            current with { TileColor = NormalizeTileColor(tileColor) },
            cancellationToken);
    }

    public async Task SaveCustomAssetAsync(
        string grevId,
        string appId,
        AppVisualAssetSlot slot,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        ValidateImage(source);

        var targetRoot = _paths.GetProfileAppPresentationRoot(grevId, appId);
        Directory.CreateDirectory(targetRoot);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        var stem = slot switch
        {
            AppVisualAssetSlot.Icon => "icon",
            AppVisualAssetSlot.TileMedia => "tile",
            AppVisualAssetSlot.HeroMedia => "hero",
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
        var fileName = stem + extension;
        var target = Path.Combine(targetRoot, fileName);
        var temporary = Path.Combine(targetRoot, $"{stem}-{Guid.NewGuid():N}{extension}.tmp");

        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        DeleteOtherSlotFiles(targetRoot, stem, target);

        var current = await ReadOverrideAsync(grevId, appId, cancellationToken);
        var updated = slot switch
        {
            AppVisualAssetSlot.Icon => current with { IconFile = fileName },
            AppVisualAssetSlot.TileMedia => current with { TileMediaFile = fileName },
            AppVisualAssetSlot.HeroMedia => current with { HeroMediaFile = fileName },
            _ => current
        };
        await WriteOverrideAsync(grevId, appId, updated, cancellationToken);
    }

    public async Task ResetAsync(
        string grevId,
        string appId,
        CancellationToken cancellationToken = default)
    {
        var root = _paths.GetProfileAppPresentationRoot(grevId, appId);
        if (!Directory.Exists(root)) return;

        await Task.Run(() => Directory.Delete(root, recursive: true), cancellationToken);
    }

    private ResolvedAppPresentation Resolve(
        GrevStorePackageDefinition package,
        string grevId,
        AppPresentationOverride overrides)
    {
        var root = _paths.GetProfileAppPresentationRoot(grevId, package.App.AppId);
        return new ResolvedAppPresentation(
            string.IsNullOrWhiteSpace(overrides.DisplayName) ? package.Presentation.DisplayName : overrides.DisplayName,
            string.IsNullOrWhiteSpace(overrides.TileColor) ? package.Presentation.TileColor : overrides.TileColor,
            ResolveOverridePath(root, overrides.IconFile) ?? package.Presentation.IconAsset,
            ResolveOverridePath(root, overrides.TileMediaFile) ?? package.Presentation.TileMediaAsset,
            ResolveOverridePath(root, overrides.HeroMediaFile) ?? package.Presentation.HeroMediaAsset,
            !IsEmpty(overrides));
    }

    private async Task<AppPresentationOverride> ReadOverrideAsync(
        string grevId,
        string appId,
        CancellationToken cancellationToken)
    {
        var metadata = _paths.GetProfileAppPresentationMetadata(grevId, appId);
        if (!File.Exists(metadata)) return new AppPresentationOverride();

        try
        {
            await using var stream = File.OpenRead(metadata);
            return await JsonSerializer.DeserializeAsync<AppPresentationOverride>(stream, _jsonOptions, cancellationToken)
                   ?? new AppPresentationOverride();
        }
        catch (JsonException)
        {
            return new AppPresentationOverride();
        }
    }

    private async Task WriteOverrideAsync(
        string grevId,
        string appId,
        AppPresentationOverride value,
        CancellationToken cancellationToken)
    {
        var metadata = _paths.GetProfileAppPresentationMetadata(grevId, appId);
        Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
        var temporary = metadata + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
            }
            File.Move(temporary, metadata, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidateImage(string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("That app artwork file no longer exists.", source);
        var extension = Path.GetExtension(source);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("App artwork must be PNG, JPG, JPEG, BMP or GIF.");
        }

        var info = new FileInfo(source);
        if (info.Length <= 0 || info.Length > MaxVisualAssetBytes)
        {
            throw new InvalidOperationException("App artwork must be larger than 0 bytes and no more than 25 MB.");
        }

        try
        {
            using var stream = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidOperationException("That file does not contain a readable image.");
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            throw new InvalidOperationException("That file is not readable app artwork.", ex);
        }
    }

    private static string? NormalizeTileColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length is not (7 or 9) || normalized[0] != '#')
        {
            throw new InvalidOperationException("Tile colour must be #RRGGBB or #AARRGGBB.");
        }

        if (!normalized[1..].All(character => Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Tile colour contains invalid hexadecimal characters.");
        }

        return normalized.ToUpperInvariant();
    }

    private static string? ResolveOverridePath(string root, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var safeName = Path.GetFileName(fileName);
        var path = Path.Combine(root, safeName);
        return File.Exists(path) ? path : null;
    }

    private static void DeleteOtherSlotFiles(string root, string stem, string keep)
    {
        foreach (var candidate in Directory.EnumerateFiles(root, stem + ".*"))
        {
            if (!string.Equals(candidate, keep, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(candidate); } catch { }
            }
        }
    }

    private static bool IsEmpty(AppPresentationOverride value) =>
        string.IsNullOrWhiteSpace(value.DisplayName) &&
        string.IsNullOrWhiteSpace(value.TileColor) &&
        string.IsNullOrWhiteSpace(value.IconFile) &&
        string.IsNullOrWhiteSpace(value.TileMediaFile) &&
        string.IsNullOrWhiteSpace(value.HeroMediaFile);
}
