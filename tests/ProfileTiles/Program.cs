// NOT VERIFIED: written without access to a .NET/WPF toolchain (this environment has no `dotnet`
// binary and Grev Home only builds on Windows). Follows the existing tests/AccountRestore/Program.cs
// pattern as closely as possible, but has never been compiled or run - a `dotnet run` on Windows is
// required before trusting it. If it fails to build, that is more likely a mismatch introduced here
// than a bug in ProfileTiles.cs/ProfileTileGridEditor.cs, which were authored in the same session.
//
// This is the C# half of the shared tile-contract regression tests. The TS half is
// scripts/verify-profile-tile-contract.mjs in grev-dad-site, which calls the REAL exported
// src/profile.ts functions. This file exercises ProfileTileGrid/ProfileTileService/
// ProfileTileGridEditor the same way, asserting the same numbers (40 tiles, 8 columns, y<=199,
// width 1-6, height 1-4) so the two sides can't quietly drift apart the way the tile Kind enum
// drifted before ProfileTiles.cs existed (see docs/PROFILE_TILES.md). Running both together on
// every change - not just once - is the actual regression protection; this only proves each side
// is internally consistent on its own build.

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
    // --- constants must match src/profile.ts exactly (GRID_COLUMNS/MAX_TILE_WIDTH/MAX_GRID_Y/MAX_TILES) ---
    Check(ProfileTileGrid.Columns == 8, "grid must be 8 columns wide");
    Check(ProfileTileGrid.MaxWidth == 6, "a tile must never be wider than 6 columns");
    Check(ProfileTileGrid.MaxHeight == 4, "a tile must never be taller than 4 rows");
    Check(ProfileTileGrid.MaxGridY == 199, "the grid must be 200 rows (0-199)");
    Check(ProfileTileGrid.MaxRows == 200, "MaxRows must be MaxGridY + 1");
    Check(ProfileTileGrid.MaxTiles == 40, "a profile must allow at most 40 tiles");

    ProfileTile BaseTile(string? tileId = null, ProfileTileKind kind = ProfileTileKind.Text,
        int x = 0, int y = 0, int width = 2, int height = 1) =>
        new(tileId ?? Guid.NewGuid().ToString(), kind, x, y, width, height, Title: "Hello");

    // --- UUID / duplicate / count ---
    Check(ProfileTileGrid.IsValidTileId(Guid.NewGuid().ToString()), "a real GUID must be a valid tile ID");
    Check(!ProfileTileGrid.IsValidTileId("not-a-guid"), "a non-UUID tile ID must be rejected");
    Check(!ProfileTileGrid.IsValidTileId(null), "a null tile ID must be rejected");

    var duplicateId = Guid.NewGuid().ToString();
    Check(ProfileTileGrid.Validate([BaseTile(duplicateId, x: 0), BaseTile(duplicateId, x: 3)]) is not null,
        "two tiles sharing the same ID must be rejected even if they don't overlap");

    var tooMany = new List<ProfileTile>();
    for (var i = 0; i < ProfileTileGrid.MaxTiles + 1; i++)
    {
        tooMany.Add(BaseTile(x: i % ProfileTileGrid.Columns, y: i, width: 1, height: 1));
    }
    Check(ProfileTileGrid.Validate(tooMany) is not null, "more than MaxTiles tiles must be rejected");

    // --- grid bounds (the reported controller Y-limit bug's server-side rule) ---
    Check(ProfileTileGrid.Validate([BaseTile(y: ProfileTileGrid.MaxGridY, height: 1)]) is null,
        "a 1-tall tile at y=199 must be accepted (the last valid row)");
    Check(ProfileTileGrid.Validate([BaseTile(y: ProfileTileGrid.MaxGridY + 1, height: 1)]) is not null,
        "a tile at y=200 must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile(y: ProfileTileGrid.MaxGridY, height: 2)]) is not null,
        "a 2-tall tile at y=199 would end at row 201 and must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile(width: ProfileTileGrid.MaxWidth + 1)]) is not null,
        "a tile wider than 6 columns must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile(x: ProfileTileGrid.Columns - 1, width: 2)]) is not null,
        "a tile that runs past column 8 must be rejected");

    // --- per-type requirements ---
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

    // --- colours / gradient angle ---
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundPrimary = "red" }]) is not null,
        "a colour must be a #RRGGBB hex value, not a CSS name");
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundAngle = 361 }]) is not null,
        "a gradient angle must be 0-360");
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundAngle = -1 }]) is not null,
        "a negative gradient angle must be rejected");
    Check(ProfileTileGrid.Validate([BaseTile() with { BackgroundAngle = 360 }]) is null,
        "a gradient angle of exactly 360 must be accepted");

    // --- collision detection ---
    var left = BaseTile(x: 0, width: 2);
    var overlapping = BaseTile(x: 1, width: 2);
    var adjacent = BaseTile(x: 2, width: 2);
    Check(ProfileTileGrid.Overlaps(left, overlapping), "two tiles sharing a column must be detected as overlapping");
    Check(!ProfileTileGrid.Overlaps(left, adjacent), "two tiles that only touch edges must not be treated as overlapping");
    Check(ProfileTileGrid.Validate([left, overlapping]) is not null, "an overlapping layout must be rejected");
    Check(ProfileTileGrid.Validate([left, adjacent]) is null, "an adjacent, non-overlapping layout must be accepted");

    // --- ProfileTileGridEditor: grid-cursor pick up / move / drop / cancel / resize ---
    var a = BaseTile(x: 0, y: 0, width: 2, height: 1);
    var b = BaseTile(x: 3, y: 0, width: 2, height: 1);
    var editor = new ProfileTileGridEditor([a, b]);
    Check(editor.Mode == ProfileTileEditorMode.Browsing, "editor must start in Browsing mode");
    Check(editor.HandleInput(InputAction.Accept), "Accept on an occupied cell must be consumed");
    Check(editor.Mode == ProfileTileEditorMode.Holding, "Accept on a tile must enter Holding mode");
    Check(editor.ActiveTile?.TileId == a.TileId, "picking up (0,0) must pick up tile a");

    editor.HandleInput(InputAction.Right);
    editor.HandleInput(InputAction.Right);
    Check(editor.ActiveTile!.X == 2, "moving right twice must advance the held tile by two cells");
    // Tile a is now at x=2..4, tile b at x=3..5 - moving right once more would overlap b and must
    // be rejected, leaving the tile exactly where it was.
    editor.HandleInput(InputAction.Right);
    Check(editor.ActiveTile!.X == 2, "a move that would overlap another tile must be rejected, not partially applied");

    editor.HandleInput(InputAction.Back);
    Check(editor.Mode == ProfileTileEditorMode.Browsing, "Back must return to Browsing");
    Check(editor.Tiles.First(t => t.TileId == a.TileId).X == 0, "Back must restore the tile's original position");

    editor.HandleInput(InputAction.Accept); // pick a back up
    Check(editor.BeginResize(), "BeginResize must succeed while Holding");
    Check(editor.Mode == ProfileTileEditorMode.Resizing, "BeginResize must enter Resizing mode");
    editor.HandleInput(InputAction.Right);
    Check(editor.ActiveTile!.Width == 3, "Right while Resizing must grow width by one column");
    editor.HandleInput(InputAction.Accept);
    Check(editor.Mode == ProfileTileEditorMode.Browsing, "Accept while Resizing must confirm and return to Browsing");
    Check(editor.Tiles.First(t => t.TileId == a.TileId).Width == 3, "the resize must be committed to the tile list");

    // --- ProfileTileService: persistence round-trip, corrupt-layout repair, media size limit ---
    var service = new ProfileTileService(paths);
    var empty = await service.GetAsync(grevId);
    Check(empty.Tiles.Count == 0, "a profile with no saved layout must return an empty layout");

    var saved = await service.SaveAsync(grevId, [BaseTile(x: 0), BaseTile(x: 3)]);
    Check(saved.Tiles.Count == 2, "SaveAsync must return the saved layout");
    var reloaded = await service.GetAsync(grevId);
    Check(reloaded.Tiles.Count == 2, "GetAsync must round-trip a saved layout");

    var invalid = new List<ProfileTile> { BaseTile(x: 0, width: 2), BaseTile(x: 1, width: 2) }; // overlapping
    var threw = false;
    try { await service.SaveAsync(grevId, invalid); }
    catch (InvalidOperationException) { threw = true; }
    Check(threw, "SaveAsync must refuse to persist an invalid (overlapping) layout");

    // A background media file over MaxBackgroundMediaBytes must be rejected, matching
    // src/profile.ts checking dataUrlByteLength(tile.backgroundMedia) against MAX_MEDIA_BYTES.
    var mediaRoot = Path.Combine(paths.GetProfilePresentation(grevId), "ProfileTiles", "Media");
    Directory.CreateDirectory(mediaRoot);
    var oversizedPath = Path.Combine(mediaRoot, "too-big.png");
    await File.WriteAllBytesAsync(oversizedPath, new byte[ProfileTileGrid.MaxBackgroundMediaBytes + 1]);
    var oversizedThrew = false;
    try
    {
        await service.SaveAsync(grevId, [BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = "too-big.png" }]);
    }
    catch (InvalidOperationException) { oversizedThrew = true; }
    Check(oversizedThrew, "a background media file over the size limit must be rejected on save");

    var okPath = Path.Combine(mediaRoot, "fine.png");
    await File.WriteAllBytesAsync(okPath, new byte[1024]);
    var withMedia = await service.SaveAsync(grevId, [BaseTile(kind: ProfileTileKind.Media) with { BackgroundMediaFile = "fine.png" }]);
    Check(withMedia.Tiles.Single().BackgroundMediaFile == "fine.png", "a background media file under the size limit must save successfully");

    Console.WriteLine("Profile tile tests passed: contract dimensions, per-type requirements, colours, collision handling, grid-cursor editing and persistence all match the intended shared contract.");
}
finally
{
    Directory.Delete(root, true);
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
