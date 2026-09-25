using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Profiles;

/// <summary>
/// Grev Home's profile tile grid. This mirrors the tile shape and grid rules grev.dad's own
/// profile tile grid uses - profileTileDefaults()/PROFILE_COLUMNS/PROFILE_MAX_WIDTH/
/// PROFILE_MAX_HEIGHT in public/profile.js, and the placement/collision math duplicated in
/// public/profile-tile-controller.js - so a tile placed on one side keeps the same meaning on the
/// other: 8-column grid, up to 200 rows, each tile 1-6 wide and 1-4 tall.
///
/// grev.dad actually has *two* different tile systems and this mirrors the grid one, not the
/// other: the small in-card tile strip (public/profile-card-tiles.js, kinds feature/link/custom,
/// a 4-column x 7-row grid capped at 4 tiles) is a separate feature that lives inside the profile
/// card itself and is out of scope here. This type is named ProfileTile rather than the tempting
/// ProfileCardTile specifically to avoid colliding with that other, unrelated concept.
/// </summary>
public enum ProfileTileKind
{
    Text,
    Link,
    Media,
    Stat
}

public enum ProfileTileBackgroundType
{
    Solid,
    Gradient,
    Media
}

public enum ProfileTileMediaFit
{
    Cover,
    Contain,
    Stretch
}

public enum ProfileTileMediaOverlay
{
    None,
    Dark,
    Light
}

public enum ProfileTileFontFamily
{
    System,
    Display,
    Mono,
    Serif,
    Rounded
}

public sealed record ProfileTile(
    string TileId,
    ProfileTileKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    string? Title = null,
    string? Body = null,
    string? LinkLabel = null,
    string? LinkUrl = null,
    string? StatValue = null,
    ProfileTileBackgroundType BackgroundType = ProfileTileBackgroundType.Solid,
    string BackgroundPrimary = "#11161d",
    string BackgroundSecondary = "#3157c9",
    int BackgroundAngle = 135,
    // Grev.dad stores this tile's picture as an inline base64 data URL, matching every other piece
    // of profile media on that side. Grev Home stores media as local files elsewhere (see
    // DashboardTileOverride.TileMediaFile / ProfileMediaDataUrl), so this holds a local filename
    // instead - a future sync layer converts between the two representations rather than this type
    // carrying a multi-megabyte string around in memory and on every JSON round-trip.
    string? BackgroundMediaFile = null,
    ProfileTileMediaFit MediaFit = ProfileTileMediaFit.Cover,
    ProfileTileMediaOverlay MediaOverlay = ProfileTileMediaOverlay.Dark,
    string TextColour = "#f4f7fb",
    string BorderColour = "#394657",
    ProfileTileFontFamily FontFamily = ProfileTileFontFamily.System);

// UpdatedAtUtc drives GrevDadProfileSyncService.SyncProfileTilesAsync's last-write-wins policy:
// it is compared against the cloud layout's own updatedAt (MAX(user_profile_tiles.updated_at) in
// src/profile.ts) to decide whether to push or pull. A fresh install's Empty layout has this unset
// (default), which always loses to a real cloud timestamp - that is what makes "sync" and "restore
// after reinstall" the same code path rather than two to keep in sync with each other.
//
// SyncedAccountUserId records which grev.dad account this layout's UpdatedAtUtc is meaningful for.
// Unlinking a profile clears its access credential but deliberately leaves this local layout file
// alone (there is nothing wrong with the tiles themselves). If a *different* grev.dad account is
// then linked to the same local GrevID, the local timestamp is from the old account and must never
// be trusted to decide "local is newer, push it" against the new account - that would overwrite
// the new account's real profile with the previous account's leftover local tiles. See
// SyncProfileTilesAsync, which is the only thing that ever sets this field.
public sealed record ProfileTileLayout(
    int SchemaVersion, IReadOnlyList<ProfileTile> Tiles, DateTimeOffset? UpdatedAtUtc = null, string? SyncedAccountUserId = null)
{
    public static ProfileTileLayout Empty { get; } = new(CurrentSchemaVersion, []);
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Pure grid math - placement, collision and validation rules. No storage, no UI, so both the
/// controller-first editor and the persistence service can share one definition of "valid layout".
///
/// This must be kept in lockstep with the server-side contract in src/profile.ts
/// (tileFromInput/validPlacement/saveProfile) - that TS file is the authoritative contract; this
/// is the C# mirror of it. See ProfileTiles.ContractParityTests for the checks that are meant to
/// catch the two drifting apart again the way the kind enum drifted before this file existed.
/// </summary>
public static class ProfileTileGrid
{
    public const int Columns = 8; // GRID_COLUMNS
    public const int MaxRows = 200; // MAX_GRID_Y (199) + 1
    public const int MaxGridY = 199; // MAX_GRID_Y - a tile's Y itself must never exceed this
    public const int MinWidth = 1;
    public const int MaxWidth = 6; // MAX_TILE_WIDTH
    public const int MinHeight = 1;
    public const int MaxHeight = 4;
    public const int MaxTiles = 40; // MAX_TILES
    public const int MaxTitleLength = 80;
    public const int MaxBodyLength = 2000;
    public const int MaxLinkLabelLength = 80;
    public const int MaxLinkUrlLength = 500;
    public const int MaxStatValueLength = 80;
    public const int MinBackgroundAngle = 0;
    public const int MaxBackgroundAngle = 360;
    // MAX_MEDIA_BYTES in src/profile.ts, applied there to the tile's inline base64 data URL.
    // ProfileTile stores media as a local file instead (see BackgroundMediaFile's doc comment), so
    // this is enforced against that file's on-disk size in ProfileTileService.SaveAsync rather
    // than here - Validate stays synchronous/IO-free, matching every other check in this class.
    public const int MaxBackgroundMediaBytes = 1_400_000;

    private static readonly System.Text.RegularExpressions.Regex UuidPattern = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HexColourPattern = new(
        "^#[0-9a-f]{6}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsValidTileId(string? tileId) => tileId is not null && UuidPattern.IsMatch(tileId);
    public static bool IsValidHexColour(string? value) => value is not null && HexColourPattern.IsMatch(value);

    public static bool Overlaps(ProfileTile a, ProfileTile b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X &&
        a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    public static bool InBounds(ProfileTile tile) =>
        tile.Width is >= MinWidth and <= MaxWidth &&
        tile.Height is >= MinHeight and <= MaxHeight &&
        tile.X >= 0 && tile.Y >= 0 && tile.Y <= MaxGridY &&
        tile.X + tile.Width <= Columns &&
        tile.Y + tile.Height <= MaxRows;

    /// <summary>
    /// Finds the first free top-left cell for a tile of the given size, scanning row-major like
    /// grev.dad's firstFreeProfilePlacement. Returns null when nothing fits.
    /// </summary>
    public static ProfileTile? FindFreePlacement(
        IReadOnlyList<ProfileTile> existing, ProfileTileKind kind, int width, int height, string? tileId = null)
    {
        for (var y = 0; y <= MaxRows - height; y++)
        {
            for (var x = 0; x <= Columns - width; x++)
            {
                var candidate = new ProfileTile(tileId ?? Guid.NewGuid().ToString("N"), kind, x, y, width, height);
                if (!existing.Any(other => Overlaps(candidate, other))) return candidate;
            }
        }
        return null;
    }

    /// <summary>Null when the layout is valid; otherwise a user-facing reason, same wording style as
    /// grev.dad's tile validation so the same mistake reads the same way on both platforms. Mirrors
    /// tileFromInput()/saveProfile() in src/profile.ts field for field - see the class doc comment.</summary>
    public static string? Validate(IReadOnlyList<ProfileTile> tiles)
    {
        if (tiles.Count > MaxTiles) return $"A profile can have up to {MaxTiles} tiles.";

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];

            if (!IsValidTileId(tile.TileId)) return "Every tile needs a valid ID.";
            if (!seenIds.Add(tile.TileId)) return "The profile contains an invalid or duplicate tile.";

            if (!InBounds(tile)) return "Every tile must stay inside the profile grid.";

            if (tile.Title is { Length: > MaxTitleLength }) return "A tile title is too long.";
            if (tile.Body is { Length: > MaxBodyLength }) return "A tile's text is too long.";
            if (tile.LinkLabel is { Length: > MaxLinkLabelLength }) return "A tile's link label is too long.";
            if (tile.LinkUrl is { Length: > MaxLinkUrlLength }) return "A tile's link is too long.";
            if (tile.StatValue is { Length: > MaxStatValueLength }) return "A tile's stat value is too long.";

            if (!IsValidHexColour(tile.BackgroundPrimary) || !IsValidHexColour(tile.BackgroundSecondary) ||
                !IsValidHexColour(tile.TextColour) || !IsValidHexColour(tile.BorderColour))
            {
                return "Every tile colour must be a valid #RRGGBB hex value.";
            }
            if (tile.BackgroundAngle is < MinBackgroundAngle or > MaxBackgroundAngle)
            {
                return "A tile's gradient angle must be between 0 and 360 degrees.";
            }
            if (tile.BackgroundType == ProfileTileBackgroundType.Media && string.IsNullOrWhiteSpace(tile.BackgroundMediaFile))
            {
                return "Every picture/GIF background needs an uploaded picture.";
            }

            switch (tile.Kind)
            {
                case ProfileTileKind.Link:
                    if (string.IsNullOrWhiteSpace(tile.LinkUrl) || !IsValidHttpUrl(tile.LinkUrl))
                        return "Every link tile needs a valid http:// or https:// URL.";
                    break;
                case ProfileTileKind.Media:
                    if (string.IsNullOrWhiteSpace(tile.BackgroundMediaFile))
                        return "Every picture/GIF tile needs an uploaded picture.";
                    break;
                case ProfileTileKind.Stat:
                    if (string.IsNullOrWhiteSpace(tile.StatValue))
                        return "Every stat tile needs a value.";
                    break;
            }

            for (var other = 0; other < index; other++)
            {
                if (Overlaps(tile, tiles[other])) return "Profile tiles cannot overlap each other.";
            }
        }
        return null;
    }

    private static bool IsValidHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Repacks tiles that have drifted into an invalid/overlapping state (e.g. after a schema
    /// change) into the first available free cells, preserving order.
    /// </summary>
    public static IReadOnlyList<ProfileTile> Compact(IReadOnlyList<ProfileTile> tiles)
    {
        var packed = new List<ProfileTile>(tiles.Count);
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
public sealed class ProfileTileService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWithStringEnums;

    public ProfileTileService(AppPaths paths) => _paths = paths;

    public async Task<ProfileTileLayout> GetAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var path = GetLayoutFile(grevId);
        if (!File.Exists(path)) return ProfileTileLayout.Empty;

        try
        {
            await using var stream = File.OpenRead(path);
            var layout = await JsonSerializer.DeserializeAsync<ProfileTileLayout>(stream, _json, cancellationToken);
            if (layout is null) return RecoverDefaults(path, "Profile tile layout contained no usable value.");
            if (layout.SchemaVersion > ProfileTileLayout.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Profile tile schema {layout.SchemaVersion} is newer than this Grev Home build supports ({ProfileTileLayout.CurrentSchemaVersion}).");
            }

            var error = ProfileTileGrid.Validate(layout.Tiles);
            if (error is null) return layout;

            // A stale/corrupt layout is repaired rather than discarded outright - the tiles
            // themselves (text, links, stats) are still worth keeping.
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

    /// <summary>
    /// syncedAccountUserId/setSyncedAccountUserId: by default (setSyncedAccountUserId: false) an
    /// ordinary local edit (the tile editor's own Save) carries the layout's existing
    /// SyncedAccountUserId forward unchanged - it has no opinion on which account a local edit
    /// belongs to. Only GrevDadProfileSyncService passes setSyncedAccountUserId: true, to actually
    /// record which grev.dad account a pulled-or-confirmed-pushed layout belongs to.
    /// </summary>
    public async Task<ProfileTileLayout> SaveAsync(
        string grevId, IReadOnlyList<ProfileTile> tiles, DateTimeOffset? updatedAtUtc = null,
        string? syncedAccountUserId = null, bool setSyncedAccountUserId = false,
        CancellationToken cancellationToken = default)
    {
        var error = ProfileTileGrid.Validate(tiles);
        if (error is not null) throw new InvalidOperationException(error);

        // Mirrors src/profile.ts checking dataUrlByteLength(tile.backgroundMedia) against
        // MAX_MEDIA_BYTES per tile. This side stores media as a file (see BackgroundMediaFile's
        // doc comment) rather than an inline data URL, so it checks the file's size on disk
        // instead - an I/O-bound check, which is why it lives here rather than in the synchronous,
        // IO-free ProfileTileGrid.Validate.
        foreach (var tile in tiles)
        {
            if (string.IsNullOrWhiteSpace(tile.BackgroundMediaFile)) continue;
            var mediaPath = Path.Combine(GetMediaRoot(grevId), Path.GetFileName(tile.BackgroundMediaFile));
            var info = new FileInfo(mediaPath);
            if (!info.Exists) throw new InvalidOperationException("A tile's picture could not be found.");
            if (info.Length > ProfileTileGrid.MaxBackgroundMediaBytes)
                throw new InvalidOperationException("A tile's picture must be no more than 1.4 MB.");
        }

        var accountUserId = setSyncedAccountUserId
            ? syncedAccountUserId
            : (await GetAsync(grevId, cancellationToken)).SyncedAccountUserId;
        var layout = new ProfileTileLayout(
            ProfileTileLayout.CurrentSchemaVersion, tiles, updatedAtUtc ?? DateTimeOffset.UtcNow, accountUserId);
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
        CleanUpOrphanedMedia(grevId, tiles);
        return layout;
    }

    /// <summary>
    /// Deletes every file under GetMediaRoot that is no longer referenced by any tile.
    ///
    /// A pulled sync and a manual media picker change both write a *new* file with a fresh GUID
    /// name for each save (see ProfileTileMediaConverter.SaveFromDataUrlAsync and
    /// MainWindow.SelectProfileTileMedia) rather than overwriting the previous one in place - so
    /// without this, replacing a tile's picture, or every sync pull that touches a media tile,
    /// leaves the old file behind permanently. Session start already triggers a sync on every
    /// launch (see MainWindow.ProfileTilesIntegration's _session.Changed handler), so this was an
    /// unbounded, indefinitely-growing disk leak, not just a one-off.
    ///
    /// Runs after a successful save, so it only ever prunes state that is genuinely superseded -
    /// never anything the layout being saved still points at.
    /// </summary>
    private void CleanUpOrphanedMedia(string grevId, IReadOnlyList<ProfileTile> tiles)
    {
        var mediaRoot = GetMediaRoot(grevId);
        if (!Directory.Exists(mediaRoot)) return;
        var referenced = new HashSet<string>(
            tiles.Where(tile => !string.IsNullOrWhiteSpace(tile.BackgroundMediaFile))
                 .Select(tile => Path.GetFileName(tile.BackgroundMediaFile)!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(mediaRoot))
        {
            if (referenced.Contains(Path.GetFileName(file))) continue;
            try { File.Delete(file); }
            catch (IOException) { /* still in use (e.g. a concurrent read) - swept on a later save */ }
            catch (UnauthorizedAccessException) { /* same as above */ }
        }
    }

    private ProfileTileLayout RecoverDefaults(string path, string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, path, "ProfileTiles", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found invalid profile tile data but could not preserve a recovery copy. The original file was left untouched.");
        }
        return ProfileTileLayout.Empty;
    }

    private string GetLayoutFile(string grevId) =>
        Path.Combine(_paths.GetProfilePresentation(grevId), "ProfileTiles", "tiles.json");

    /// <summary>Where a tile's BackgroundMediaFile is resolved from/written to. Public so
    /// GrevDadProfileSyncService can save a pulled tile's picture into the same place SaveAsync
    /// itself checks it against (ProfileTileGrid.MaxBackgroundMediaBytes).</summary>
    public string GetMediaRoot(string grevId) =>
        Path.Combine(_paths.GetProfilePresentation(grevId), "ProfileTiles", "Media");
}

/// <summary>
/// Converts a tile's background picture between grev.dad's inline base64 data URL and Grev Home's
/// local-file convention (see ProfileTile.BackgroundMediaFile's doc comment). Used only by
/// GrevDadProfileSyncService - everything else on this side already works in local files.
/// </summary>
public static class ProfileTileMediaConverter
{
    private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/gif"] = ".gif", ["image/webp"] = ".webp"
    };

    /// <summary>Cloud -> local. Decodes a "data:&lt;mime&gt;;base64,&lt;...&gt;" string, enforces
    /// the same size limit the server enforces on the data URL itself and SaveAsync enforces on
    /// the resulting file, and writes it under ProfileTileService.GetMediaRoot. Returns the file
    /// name to store in ProfileTile.BackgroundMediaFile.</summary>
    public static async Task<string> SaveFromDataUrlAsync(
        string mediaRoot, string dataUrl, CancellationToken cancellationToken = default)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A synced tile picture was not a valid data URL.");
        }
        var header = dataUrl[5..comma]; // "image/png;base64"
        var mime = header.Split(';')[0];
        if (!ExtensionByMime.TryGetValue(mime, out var extension))
        {
            throw new InvalidDataException($"A synced tile picture used an unsupported image type ({mime}).");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("A synced tile picture was not valid base64.", ex);
        }
        if (bytes.Length == 0 || bytes.Length > ProfileTileGrid.MaxBackgroundMediaBytes)
        {
            throw new InvalidDataException("A synced tile picture exceeded the 1.4 MB limit.");
        }

        Directory.CreateDirectory(mediaRoot);
        var fileName = $"tile-{Guid.NewGuid():N}{extension}";
        var target = Path.Combine(mediaRoot, fileName);
        var temporary = target + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return fileName;
    }

    /// <summary>Local -> cloud. Animated GIFs are sent byte-for-byte so Grev Home never flattens
    /// them to a single PNG frame. Other image types keep using ProfileMediaDataUrl's existing
    /// downscale/PNG path, which is shared with avatar/banner sync.</summary>
    public static string? ReadAsDataUrl(string mediaRoot, string? mediaFileName)
    {
        if (string.IsNullOrWhiteSpace(mediaFileName)) return null;
        var sourcePath = Path.Combine(mediaRoot, Path.GetFileName(mediaFileName));
        if (!File.Exists(sourcePath)) return null;

        if (string.Equals(Path.GetExtension(sourcePath), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            var info = new FileInfo(sourcePath);
            if (info.Length <= 0 || info.Length > ProfileTileGrid.MaxBackgroundMediaBytes)
                throw new IOException("A tile's animated GIF must be no more than 1.4 MB.");
            return $"data:image/gif;base64,{Convert.ToBase64String(File.ReadAllBytes(sourcePath))}";
        }

        // ProfileTileService.GetMediaRoot is not GetProfileRoot, so use the full-path helper for
        // static formats. BMP in particular is intentionally converted to PNG because Grev.dad's
        // profile tile API does not accept BMP data URLs.
        return ProfileMediaDataUrl.TryReadFile(sourcePath);
    }
}
