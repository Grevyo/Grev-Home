// NOT VERIFIED locally: Grev Home builds/tests on Windows. This is the C# half of the shared
// profile-tile contract regression coverage; the grev.dad half runs its real src/profile.ts rules.

using System.IO;
using GrevHome.Input;
using GrevHome.Profiles;
using GrevHome.Storage;

var root = Path.Combine(Path.GetTempPath(), "GrevHome-profile-tiles-test-" + Guid.NewGuid().ToString("N"));
var paths = new AppPaths(root);
const string grevId = "GTESTLOCAL";
paths.EnsureProfileLayout(grevId);

try
{
    Check(ProfileTileGrid.Columns == 8, "grid must be 8 columns wide");
    Check(ProfileTileGrid.MaxWidth == 6, "a tile must never be wider than 6 columns");
    Check(ProfileTileGrid.MaxHeight == 4, "a tile must never be taller than 4 rows");
    Check(ProfileTileGrid.MaxGridY == 199, "the grid must be 200 rows (0-199)");
    Check(ProfileTileGrid.MaxRows == 200, "MaxRows must be MaxGridY + 1");
    Check(ProfileTileGrid.MaxTiles == 40, "a profile must allow at most 40 tiles");

    ProfileTile BaseTile(string? tileId = null, ProfileTileKind kind = ProfileTileKind.Text,
        int x = 0, int y = 0, int width = 2, int height = 1) =>
        new(tileId ?? Guid.NewGuid().ToString(), kind, x, y, width, height, Title: "Hello");

    Check(ProfileTileGrid.IsValidTileId(Guid.NewGuid().ToString()), "a real GUID must be a valid tile ID");
    Check(!ProfileTileGrid.IsValidTileId("not-a-guid"), "a non-UUID tile ID must be rejected");
    Check(!ProfileTileGrid.IsValidTileId(null), "a null tile ID must be rejected");

    var duplicateId = Guid.NewGuid().ToString();
    Check(ProfileTileGrid.Validate([BaseTile(duplicateId, x: 0), BaseTile(duplicateId, x: 3)]) is not null,
        "two tiles sharing the same ID must be rejected even if they don't overlap");

    var tooMany = new List<ProfileTile>();
    for (var i = 0; i < ProfileTileGrid.MaxTiles + 1; i++)
        tooMany.Add(BaseTile(x: i % ProfileTileGrid.Columns, y: i, width: 1, height: 1));
    Check(ProfileTileGrid.Validate(tooMany) is not null, "more than MaxTiles tiles must be rejected");

    Check(ProfileTileGrid.Validate([BaseTile(y: ProfileTileGrid.MaxGridY, height: 1)]) is null,
        "a 1-tall tile at y=199 must be accepted");
    Check(ProfileTileGrid.Validate([BaseTile(y: ProfileTileGrid.MaxGridY + 1, height: 1)]) is not null,
        "a tile at y=200 must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile(y: ProfileTileGrid.MaxGridY, height: 2)]) is not null,
        "a 2-tall tile at y=199 must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile(width: ProfileTileGrid.MaxWidth + 1)]) is not null,
        "a tile wider than 6 columns must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile(x: ProfileTileGrid.Columns - 1, width: 2)]) is not null,
        "a tile that runs past column 8 must be rejected");

    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Link) with { LinkUrl = null }]) is not null,
        "a link tile needs a URL");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Link) with { LinkUrl = "javascript:alert(1)" }]) is not null,
        "a link tile must reject non-http(s) URLs");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Link) with { LinkUrl = "https://example.com" }]) is null,
        "a link tile with a valid https URL must be accepted");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Stat) with { StatValue = null }]) is not null,
        "a stat tile needs a value");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Stat) with { StatValue = "100%" }]) is null,
        "a stat tile with a value must be accepted");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = null }]) is not null,
        "a media tile needs an uploaded picture");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = "tile.png" }]) is null,
        "a media tile with a picture must be accepted");

    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundPrimary = "red" }]) is not null,
        "a colour must be #RRGGBB");
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundAngle = 361 }]) is not null,
        "a gradient angle must be 0-360");
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundAngle = -1 }]) is not null,
        "a negative gradient angle must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundAngle = 360 }]) is null,
        "360 degrees must be accepted");

    var left = BaseTile(x: 0, width: 2);
    var overlapping = BaseTile(x: 1, width: 2);
    var adjacent = BaseTile(x: 2, width: 2);
    Check(ProfileTileGrid.Overlaps(left, overlapping), "overlap must be detected");
    Check(!ProfileTileGrid.Overlaps(left, adjacent), "edge-touching tiles must not overlap");
    Check(ProfileTileGrid.Validate([left, overlapping]) is not null, "an overlapping layout must fail");
    Check(ProfileTileGrid.Validate([left, adjacent]) is null, "an adjacent layout must pass");

    var a = BaseTile(x: 0, y: 0, width: 2, height: 1);
    var b = BaseTile(x: 3, y: 0, width: 2, height: 1);
    var editor = new ProfileTileGridEditor([a, b]);
    Check(editor.Mode == ProfileTileEditorMode.Browsing, "editor starts browsing");
    Check(editor.HandleInput(InputAction.Accept), "Accept on a tile is consumed");
    Check(editor.Mode == ProfileTileEditorMode.Holding, "Accept picks the tile up");
    editor.HandleInput(InputAction.Right);
    Check(editor.ActiveTile!.X == 1, "first right move should reach x=1");
    editor.HandleInput(InputAction.Right);
    Check(editor.ActiveTile!.X == 1, "second right move would overlap b and must be rejected");
    editor.HandleInput(InputAction.Back);
    Check(editor.Tiles.First(t => t.TileId == a.TileId).X == 0, "Back restores origin");

    editor.HandleInput(InputAction.Accept);
    Check(editor.BeginResize(), "BeginResize succeeds while holding");
    editor.HandleInput(InputAction.Right);
    Check(editor.ActiveTile!.Width == 3, "resize grows width by one");
    editor.HandleInput(InputAction.Accept);
    Check(editor.Tiles.First(t => t.TileId == a.TileId).Width == 3, "resize commits");

    var fallbackEditor = new ProfileTileGridEditor([BaseTile(x: 0, y: 0, width: 1, height: 1)]);
    var added = fallbackEditor.AddTile(ProfileTileKind.Text, 1, 1);
    Check(added is not null, "fallback add should find a free cell");
    Check(ProfileTileGrid.IsValidTileId(added!.TileId), "fallback-added tile must use a valid dashed UUID");

    var service = new ProfileTileService(paths);
    var empty = await service.GetAsync(grevId);
    Check(empty.Tiles.Count == 0, "a profile with no saved layout returns empty");
    var saved = await service.SaveAsync(grevId, [BaseTile(x: 0), BaseTile(x: 3)]);
    Check(saved.Tiles.Count == 2, "SaveAsync returns saved layout");
    var reloaded = await service.GetAsync(grevId);
    Check(reloaded.Tiles.Count == 2, "GetAsync round-trips saved layout");

    var styled = BaseTile() with
    {
        Title = "Styled",
        Body = "Same fields as grev.dad",
        BackgroundType = ProfileTileBackgroundType.Gradient,
        BackgroundPrimary = "#123456",
        BackgroundSecondary = "#abcdef",
        BackgroundAngle = 225,
        MediaFit = ProfileTileMediaFit.Contain,
        MediaOverlay = ProfileTileMediaOverlay.Light,
        TextColour = "#fedcba",
        BorderColour = "#654321",
        FontFamily = ProfileTileFontFamily.Mono
    };
    await service.SaveAsync(grevId, [styled]);
    var styledReloaded = (await service.GetAsync(grevId)).Tiles.Single();
    Check(styledReloaded.Body == styled.Body, "tile body must round-trip");
    Check(styledReloaded.BackgroundType == ProfileTileBackgroundType.Gradient, "background type must round-trip");
    Check(styledReloaded.BackgroundPrimary == "#123456" && styledReloaded.BackgroundSecondary == "#abcdef", "gradient colours must round-trip");
    Check(styledReloaded.BackgroundAngle == 225, "gradient angle must round-trip");
    Check(styledReloaded.MediaFit == ProfileTileMediaFit.Contain && styledReloaded.MediaOverlay == ProfileTileMediaOverlay.Light, "media presentation must round-trip");
    Check(styledReloaded.TextColour == "#fedcba" && styledReloaded.BorderColour == "#654321", "text and border colours must round-trip");
    Check(styledReloaded.FontFamily == ProfileTileFontFamily.Mono, "font family must round-trip");

    var invalid = new List<ProfileTile> { BaseTile(x: 0, width: 2), BaseTile(x: 1, width: 2) };
    var threw = false;
    try { await service.SaveAsync(grevId, invalid); }
    catch (InvalidOperationException) { threw = true; }
    Check(threw, "SaveAsync refuses overlapping layout");

    var mediaRoot = service.GetMediaRoot(grevId);
    Directory.CreateDirectory(mediaRoot);
    var oversizedPath = Path.Combine(mediaRoot, "too-big.png");
    await File.WriteAllBytesAsync(oversizedPath, new byte[ProfileTileGrid.MaxBackgroundMediaBytes + 1]);
    var oversizedThrew = false;
    try
    {
        await service.SaveAsync(grevId, [BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = "too-big.png" }]);
    }
    catch (InvalidOperationException) { oversizedThrew = true; }
    Check(oversizedThrew, "oversized background media is rejected");

    var okPath = Path.Combine(mediaRoot, "fine.png");
    await File.WriteAllBytesAsync(okPath, new byte[1024]);
    var withMedia = await service.SaveAsync(grevId, [BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = "fine.png" }]);
    Check(withMedia.Tiles.Single().BackgroundMediaFile == "fine.png", "valid media persists");

    var gifBytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    var gifPath = Path.Combine(mediaRoot, "animated.gif");
    await File.WriteAllBytesAsync(gifPath, gifBytes);
    var gifDataUrl = ProfileTileMediaConverter.ReadAsDataUrl(mediaRoot, "animated.gif");
    Check(gifDataUrl?.StartsWith("data:image/gif;base64,", StringComparison.Ordinal) == true,
        "animated GIF sync must keep the image/gif MIME type");
    var gifPayload = gifDataUrl![(gifDataUrl.IndexOf(',') + 1)..];
    Check(Convert.FromBase64String(gifPayload).SequenceEqual(gifBytes),
        "animated GIF sync must preserve the original bytes rather than flattening to PNG");

    // --- orphaned media cleanup: a save must delete media files no tile references any more ---
    var orphanRoot = service.GetMediaRoot(grevId);
    var keptPath = Path.Combine(orphanRoot, "kept.png");
    var orphanedPath = Path.Combine(orphanRoot, "orphaned.png");
    await File.WriteAllBytesAsync(keptPath, new byte[512]);
    await File.WriteAllBytesAsync(orphanedPath, new byte[512]);
    await service.SaveAsync(grevId, [BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = "kept.png" }]);
    Check(File.Exists(keptPath), "a save must never delete media a tile still references");
    Check(!File.Exists(orphanedPath),
        "a save must delete media files no tile references any more (previously leaked indefinitely - every sync pull and every tile picture change wrote a new file and never removed the old one)");

    // --- duplicate tile ---
    var originalTile = BaseTile(x: 0, y: 0, width: 2, height: 1) with { Title = "Original" };
    var dupEditor = new ProfileTileGridEditor([originalTile]);
    dupEditor.HandleInput(InputAction.Accept);
    var duplicate = dupEditor.DuplicateActiveTile();
    Check(duplicate is not null, "duplicating a held tile must succeed when there is free space");
    Check(duplicate!.Title == "Original", "a duplicate must copy the source tile's content");
    Check(duplicate.TileId != originalTile.TileId, "a duplicate must get its own tile ID, not share the source's");
    Check(dupEditor.Tiles.Count == 2, "duplicating must add a second tile, not replace the first");
    Check(dupEditor.Tiles.Any(t => t.TileId == originalTile.TileId), "the original tile must remain after duplicating");
    Check(dupEditor.ActiveTile!.TileId == duplicate.TileId, "duplicating must hold the new copy, not the original");
    Check(ProfileTileGrid.Validate(dupEditor.Tiles) is null, "the original and its duplicate must never overlap");
    var noSpaceEditor = new ProfileTileGridEditor([BaseTile(x: 0, y: 0, width: 6, height: 4)]);
    // A single 6x4 tile does not literally fill the whole 8x200 grid, so duplicating it should
    // still find room elsewhere - this only exercises that a duplicate is placed somewhere new,
    // not the "completely full grid" edge case (which FindFreePlacement's own contract covers).
    noSpaceEditor.HandleInput(InputAction.Accept);
    Check(noSpaceEditor.DuplicateActiveTile() is not null, "duplicating must find free space elsewhere on a large grid");

    // --- IsDirty / MarkSaved: the "confirm before discarding unsaved changes" safeguard ---
    var dirtyEditor = new ProfileTileGridEditor([BaseTile(x: 0, y: 0)]);
    Check(!dirtyEditor.IsDirty, "a freshly loaded editor must not be dirty");
    dirtyEditor.HandleInput(InputAction.Accept);
    dirtyEditor.HandleInput(InputAction.Right);
    Check(dirtyEditor.IsDirty, "moving a tile must mark the editor dirty");
    dirtyEditor.HandleInput(InputAction.Back);
    Check(!dirtyEditor.IsDirty, "cancelling a move (Back) must return to not-dirty, not stay dirty");
    dirtyEditor.HandleInput(InputAction.Accept);
    dirtyEditor.HandleInput(InputAction.Right);
    dirtyEditor.HandleInput(InputAction.Accept);
    Check(dirtyEditor.IsDirty, "a confirmed move must stay dirty");
    dirtyEditor.MarkSaved();
    Check(!dirtyEditor.IsDirty, "MarkSaved must rebase the dirty baseline onto the current layout");

    Console.WriteLine("Profile tile tests passed.");
}
finally
{
    Directory.Delete(root, true);
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
