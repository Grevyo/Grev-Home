using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Profiles;

/// <summary>
/// Grev Home's profile tile grid. This mirrors the tile shape and grid rules grev.dad's web
/// profile editor already uses (src/profile-card-tiles.ts and the freePlacement/tileLayoutError/
/// compactTiles logic in public/profile-tile-save-fix.js and public/dashboard-freeze-fix.js), so a
/// tile placed on one side keeps the same meaning on the other. Kept intentionally smaller than
/// grev.dad's full styling set (no background/media/border customization yet) - this is the grid
/// and persistence foundation a controller-first tile editor is built on; presentation options can
/// grow later without changing the placement contract.
/// </summary>
public enum ProfileCardTileKind
{
    Feature,
    Link,
    Custom
}

public sealed record ProfileCardTile(
    string TileId,
    ProfileCardTileKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    string? FeatureId = null,
    string? Title = null,
    string? Description = null,
    string? LinkUrl = null);

public sealed record ProfileCardTileLayout(int SchemaVersion, IReadOnlyList<ProfileCardTile> Tiles)
{
    public static ProfileCardTileLayout Empty { get; } = new(CurrentSchemaVersion, []);
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Pure grid math - placement, collision and compaction rules. No storage, no UI, so both the
/// controller-first editor and the persistence service can share one definition of "valid layout"
/// with grev.dad's rules (same GRID_COLUMNS=8 / width 1-6 / height 1-4 as profile-tile-save-fix.js).
/// </summary>
public static class ProfileTileGrid
{
    public const int Columns = 8;
    public const int MaxRows = 200;
    public const int MinWidth = 1;
    public const int MaxWidth = 6;
    public const int MinHeight = 1;
    public const int MaxHeight = 4;

    public static bool Overlaps(ProfileCardTile a, ProfileCardTile b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X &&
        a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    public static bool InBounds(ProfileCardTile tile) =>
        tile.Width is >= MinWidth and <= MaxWidth &&
        tile.Height is >= MinHeight and <= MaxHeight &&
        tile.X >= 0 && tile.Y >= 0 &&
        tile.X + tile.Width <= Columns &&
        tile.Y + tile.Height <= MaxRows;

    /// <summary>
    /// Finds the first free top-left cell for a tile of the given size, scanning row-major like
    /// grev.dad's freePlacement. Returns null when nothing fits.
    /// </summary>
    public static ProfileCardTile? FindFreePlacement(
        IReadOnlyList<ProfileCardTile> existing, ProfileCardTileKind kind, int width, int height, string? tileId = null)
    {
        for (var y = 0; y <= MaxRows - height; y++)
        {
            for (var x = 0; x <= Columns - width; x++)
            {
                var candidate = new ProfileCardTile(tileId ?? Guid.NewGuid().ToString("N"), kind, x, y, width, height);
                if (!existing.Any(other => Overlaps(candidate, other))) return candidate;
            }
        }
        return null;
    }

    /// <summary>Null when the layout is valid; otherwise a user-facing reason, same wording style as
    /// tileLayoutError() on the web so the same mistake reads the same way on both platforms.</summary>
    public static string? Validate(IReadOnlyList<ProfileCardTile> tiles)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            if (!InBounds(tile)) return "Every tile must stay inside the profile grid.";
            if (tile.Kind == ProfileCardTileKind.Link &&
                (string.IsNullOrWhiteSpace(tile.LinkUrl) ||
                 !(tile.LinkUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   tile.LinkUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))))
            {
                return "Every link tile needs a valid http:// or https:// URL.";
            }
            for (var other = 0; other < index; other++)
            {
                if (Overlaps(tile, tiles[other])) return "Profile tiles cannot overlap each other.";
            }
        }
        return null;
    }

    /// <summary>
    /// Repacks tiles that have drifted into an invalid/overlapping state (e.g. after a schema
    /// change) into the first available free cells, preserving order. Mirrors compactTiles() in
    /// dashboard-freeze-fix.js.
    /// </summary>
    public static IReadOnlyList<ProfileCardTile> Compact(IReadOnlyList<ProfileCardTile> tiles)
    {
        var packed = new List<ProfileCardTile>(tiles.Count);
        foreach (var source in tiles)
        {
            var width = Math.Clamp(source.Width, MinWidth, MaxWidth);
            var height = Math.Clamp(source.Height, MinHeight, MaxHeight);
            var placement = FindFreePlacement(packed, source.Kind, width, height, source.TileId);
            packed.Add(placement is null
                ? source with { Width = width, Height = height }
                : source with { Width = width, Height = height, X = placement.X, Y = placement.Y });
        }
        return packed;
    }
}

/// <summary>
/// Per-GrevID tile layout storage. Same shape as ProfilePresentationSettingsService: atomic
/// temp-then-move JSON under the profile's presentation root.
/// </summary>
public sealed class ProfileCardTileService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWithStringEnums;

    public ProfileCardTileService(AppPaths paths) => _paths = paths;

    public async Task<ProfileCardTileLayout> GetAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var path = GetLayoutFile(grevId);
        if (!File.Exists(path)) return ProfileCardTileLayout.Empty;

        try
        {
            await using var stream = File.OpenRead(path);
            var layout = await JsonSerializer.DeserializeAsync<ProfileCardTileLayout>(stream, _json, cancellationToken);
            if (layout is null) return RecoverDefaults(path, "Profile tile layout contained no usable value.");
            if (layout.SchemaVersion > ProfileCardTileLayout.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Profile tile schema {layout.SchemaVersion} is newer than this Grev Home build supports ({ProfileCardTileLayout.CurrentSchemaVersion}).");
            }

            var error = ProfileTileGrid.Validate(layout.Tiles);
            if (error is null) return layout;

            // A stale/corrupt layout is repaired rather than discarded outright - the tiles
            // themselves (feature choices, links, custom titles) are still worth keeping.
            var repaired = ProfileTileGrid.Compact(layout.Tiles);
            return ProfileTileGrid.Validate(repaired) is null
                ? layout with { Tiles = repaired }
                : RecoverDefaults(path, $"Profile tile layout could not be repaired: {error}");
        }
        catch (JsonException ex)
        {
            return RecoverDefaults(path, $"Profile tile layout JSON could not be parsed: {ex.Message}");
        }
    }

    public async Task<ProfileCardTileLayout> SaveAsync(
        string grevId, IReadOnlyList<ProfileCardTile> tiles, CancellationToken cancellationToken = default)
    {
        var error = ProfileTileGrid.Validate(tiles);
        if (error is not null) throw new InvalidOperationException(error);

        var layout = new ProfileCardTileLayout(ProfileCardTileLayout.CurrentSchemaVersion, tiles);
        var path = GetLayoutFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, layout, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return layout;
    }

    private ProfileCardTileLayout RecoverDefaults(string path, string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, path, "ProfileCardTiles", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found invalid profile tile data but could not preserve a recovery copy. The original file was left untouched.");
        }
        return ProfileCardTileLayout.Empty;
    }

    private string GetLayoutFile(string grevId) =>
        Path.Combine(_paths.GetProfilePresentation(grevId), "ProfileTiles", "tiles.json");
}
