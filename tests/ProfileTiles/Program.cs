// NOT VERIFIED locally: Grev Home builds/tests on Windows. This is the C# half of the shared
// profile-tile contract regression coverage; the grev.dad half runs its real src/profile.ts rules.

using System.IO;
using System.Text.Json;
using GrevHome.Input;
using GrevHome.Online;
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

    // --- live widget tiles (mirrors src/profile-widgets.ts) -------------------------------------
    Check(ProfileWidgets.All.Select(info => info.WireName).SequenceEqual(new[]
        { "recent-games", "game-activity", "most-played", "favourite-games", "best-friends", "bio", "stats", "achievements", "retroachievements" }),
        "widget kinds and their order must match PROFILE_WIDGETS in grev.dad");
    foreach (var info in ProfileWidgets.All)
        Check(ProfileWidgets.FromWire(ProfileWidgets.ToWire(info.Kind)) == info.Kind, $"{info.WireName} must round-trip");
    Check(ProfileWidgets.FromWire("something-new") is null, "an unknown widget kind degrades to a plain tile");
    Check(ProfileWidgets.EffectiveCount(ProfileWidgetKind.RecentGames, null) == 6, "recent games default to 6 items");
    Check(ProfileWidgets.EffectiveCount(ProfileWidgetKind.RecentGames, 40) == 12, "widget counts clamp to 12");

    var widgetTile = BaseTile() with { Widget = ProfileWidgetKind.Stats };
    Check(ProfileTileGrid.Validate([widgetTile]) is null, "a widget on a text tile is valid");
    Check(ProfileTileGrid.Validate([BaseTile(kind: ProfileTileKind.Link) with { LinkUrl = "https://grev.dad", Widget = ProfileWidgetKind.Stats }]) is not null,
        "widgets must be text tiles, like grev.dad");
    Check(ProfileTileGrid.Validate([widgetTile with { Widget = ProfileWidgetKind.RecentGames, WidgetCount = 13 }]) is not null,
        "a widget count above 12 is rejected");

    var wire = GrevDadProfileSyncService.ToWireTile(widgetTile with { Widget = ProfileWidgetKind.RecentGames, WidgetCount = 3 }, null);
    Check(wire.TileType == "text" && wire.Widget == "recent-games" && wire.WidgetConfig?.Count == 3, "widget tiles sync as text tiles with widget + config");
    var back = GrevDadProfileSyncService.FromWireTile(wire, null);
    Check(back.Widget == ProfileWidgetKind.RecentGames && back.WidgetCount == 3, "widget tiles round-trip through the sync wire");
    var plainWire = GrevDadProfileSyncService.ToWireTile(BaseTile(), null);
    Check(plainWire.Widget is null && plainWire.WidgetConfig is null, "plain tiles send no widget");
    var unknown = GrevDadProfileSyncService.FromWireTile(wire with { Widget = "future-widget" }, null);
    Check(unknown.Widget is null && unknown.WidgetCount is null, "a widget this build does not know syncs down as a plain tile");

    // Widget data from grev.dad (shapes from src/profile-unified.ts resolveWidgets).
    JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var recent = ProfileWidgetViews.FromServer(ProfileWidgetKind.RecentGames, Json($$"""
        {"widget":"recent-games","items":[
          {"appId":"retroarch","appName":"RetroArch","contentId":"snes:smw","title":"Super Mario World","lastPlayedAt":{{now - 7200}},"totalSeconds":5400,"sessions":1},
          {"appId":"steam-1","appName":"Halo","contentId":null,"title":"Halo","lastPlayedAt":{{now - 60}},"totalSeconds":60,"sessions":1},
          {"appId":"x","appName":"X","title":"Third","lastPlayedAt":{{now}},"totalSeconds":1,"sessions":1}]}
        """), 2, isSelf: false);
    Check(recent.Lines.Count == 2, "the item count limits the list");
    Check(recent.Lines[0].Title == "Super Mario World" && recent.Lines[0].Detail == "RetroArch • 2 h ago", "games show the content name, then the emulator");
    Check(recent.Lines[1].Detail == "just now", "a game whose title is its app name does not repeat it");
    var hidden = ProfileWidgetViews.FromServer(ProfileWidgetKind.GameActivity, Json("""{"widget":"game-activity","hidden":true,"reason":"friends-only"}"""), 5, false);
    Check(hidden.EmptyText == "Shared with friends only." && hidden.Lines.Count == 0, "friends-only widgets say so instead of showing nothing");
    var activity = ProfileWidgetViews.FromServer(ProfileWidgetKind.GameActivity, Json("""{"nowPlaying":{"title":"Zelda"},"sessions":[]}"""), 5, false);
    Check(activity.Highlight == "Now playing Zelda" && activity.EmptyText is null, "now playing shows as the highlight");
    var best = ProfileWidgetViews.FromServer(ProfileWidgetKind.BestFriends, Json("""{"items":[{"userId":"u1","displayName":"Bob","avatarMedia":null,"availability":"online","activityText":"Halo"}]}"""), 6, false);
    Check(best.Lines.Single() is { IsPerson: true, UserId: "u1", Availability: "online", Detail: "Halo" }, "best friends carry who to open and their presence");
    var stats = ProfileWidgetViews.FromServer(ProfileWidgetKind.Stats, Json("""{"level":3,"totalXp":1200,"totalTrackedSeconds":null,"completedSessions":4,"uniqueApps":2,"achievements":1}"""), 0, false);
    Check(stats.Stats.Select(stat => stat.Label).SequenceEqual(new[] { "Level", "XP", "Sessions", "Games", "Achievements" }),
        "stats a member hides (null) are left out");
    var ra = ProfileWidgetViews.FromServer(ProfileWidgetKind.RetroAchievements, Json("""
        {"linked":true,"username":"AliceRA","summary":{"username":"AliceRA","totalPoints":1234,"rank":42,"richPresence":"Exploring Hyrule",
          "recentlyPlayed":[],"recentAchievements":[{"title":"Sword","gameTitle":"Zelda","points":5,"badgeUrl":"https://media.retroachievements.org/Badge/1.png","hardcore":true}]}}
        """), 5, false);
    Check(ra.Subtitle == "AliceRA • 1,234 points • rank 42" && ra.Highlight == "Exploring Hyrule", "RetroAchievements shows points, rank and presence");
    Check(ra.Lines.Single().ImageUrl!.StartsWith("https://media.retroachievements.org/"), "achievement badges come from RetroAchievements");
    var raUnlinked = ProfileWidgetViews.FromServer(ProfileWidgetKind.RetroAchievements, Json("""{"linked":false}"""), 5, isSelf: true);
    Check(raUnlinked.EmptyText!.Contains("Edit Profile"), "the owner is told where to link RetroAchievements");
    var siteAchievements = ProfileWidgetViews.FromServer(ProfileWidgetKind.Achievements, Json("""{"earned":1,"available":9,"items":[{"name":"First Boot","description":"d","imageUrl":"/achievement-badges/x.svg"}]}"""), 6, false);
    Check(siteAchievements.Lines.Single().ImageUrl == "https://grev.dad/achievement-badges/x.svg", "grev.dad badge paths become absolute URLs");

    var localStats = new ProfileStatsSnapshot(
        new ProfileLevelProgress(2, 600, 100, 500, 20), 3600, 3, 0, 2, DateTimeOffset.UtcNow,
        [new ProfileTopAppStat("a", "Halo", 3000, 2, false, DateTimeOffset.UtcNow)],
        [new ProfileRecentActivityStat("a", "Halo", 3000, 2, true, DateTimeOffset.UtcNow)],
        [], []);
    var offlineActivity = ProfileWidgetViews.FromLocal(ProfileWidgetKind.GameActivity, 5, localStats, null, []);
    Check(offlineActivity.Highlight == "Now playing Halo", "offline game activity shows what is running on this PC");
    var offlineMost = ProfileWidgetViews.FromLocal(ProfileWidgetKind.MostPlayed, 5, localStats, null, []);
    Check(offlineMost.Lines.Single().Detail == "50m • 2 sessions", "offline most played uses local play time");
    var offlineBest = ProfileWidgetViews.FromLocal(ProfileWidgetKind.BestFriends, 5, localStats, null, []);
    Check(offlineBest.EmptyText!.Contains("Link Grev.dad"), "online-only widgets explain why they are empty");

    // A real profile document from grev.dad deserializes into the client's records and draws.
    var documentJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "profile-document.json"));
    var envelope = JsonSerializer.Deserialize<ProfileDocumentApiResponse>(documentJson, JsonDefaults.IndentedWeb)!;
    var document = envelope.Profile!;
    Check(document.Relationship == "friend" && !document.IsSelf, "the friend document says who is looking");
    Check(document.Card.Headline == "Retro collector", "the shared headline comes through");
    Check(document.Presence?.ActivityText == "Super Mario World", "presence comes through");
    var documentTiles = document.ToTiles();
    Check(documentTiles.Count == 8 && documentTiles.All(tile => tile.Widget is not null), "every widget tile keeps its widget");
    Check(ProfileTileGrid.Validate(documentTiles) is null, "grev.dad's tiles are valid in Grev Home");
    foreach (var tile in documentTiles)
    {
        var view = document.WidgetView(tile)!;
        Check(view.Kind == tile.Widget, $"{tile.Widget} resolves to its own view");
    }
    var documentRecent = document.WidgetView(documentTiles.First(tile => tile.Widget == ProfileWidgetKind.RecentGames))!;
    Check(documentRecent.Lines.Count == 3 && documentRecent.Lines.Any(line => line.Title == "Shadow of the Colossus"), "recent games come from grev.dad's session history");
    var documentBest = document.WidgetView(documentTiles.First(tile => tile.Widget == ProfileWidgetKind.BestFriends))!;
    Check(documentBest.Lines.Count == 1 && documentBest.Lines[0].UserId is not null, "best friends can be opened");

    // Identity sync decisions (GrevDadIdentitySyncService).
    Check(GrevDadIdentitySyncService.Decide("L", "R", null, null, remoteHasValue: true, localHasValue: true) == IdentitySyncDirection.Pull,
        "the first sync after linking brings the grev.dad profile over");
    Check(GrevDadIdentitySyncService.Decide("L", "", null, null, remoteHasValue: false, localHasValue: true) == IdentitySyncDirection.Push,
        "the first sync uploads a field grev.dad does not have yet");
    Check(GrevDadIdentitySyncService.Decide("L2", "R", "L1", "R", true, true) == IdentitySyncDirection.Push, "a local edit is uploaded");
    Check(GrevDadIdentitySyncService.Decide("L", "R2", "L", "R1", true, true) == IdentitySyncDirection.Pull, "a website edit comes down");
    Check(GrevDadIdentitySyncService.Decide("L", "R", "L", "R", true, true) == IdentitySyncDirection.None, "nothing changed, nothing moves");
    Check(GrevDadIdentitySyncService.Decide("L2", "R2", "L1", "R1", true, true) == IdentitySyncDirection.Push, "if both changed, this PC's edit wins");

    // Favourite games: offline queue, then grev.dad's list wins after the queue is sent.
    var favourites = new FavouriteGameService(paths);
    var mario = new FavouriteGame("grevhome:mario", "Super Mario World", "SNES");
    Check(await favourites.ToggleAsync(grevId, mario, queueForSync: true), "starring a game makes it a favourite");
    Check(await favourites.IsFavouriteAsync(grevId, "GREVHOME:MARIO"), "favourite keys match case-insensitively");
    var queued = await favourites.GetAsync(grevId);
    Check(queued.Pending.Single().Kind == FavouriteChangeKind.Add, "a linked profile queues the change for grev.dad");
    var halo = new FavouriteGame("web:halo", "Halo");
    await favourites.ApplyRemoteAsync(grevId, [mario, halo], queued.Pending);
    var synced = await favourites.GetAsync(grevId);
    Check(synced.Pending.Count == 0 && synced.Games.Count == 2, "after sync the list matches grev.dad, including games starred on the website");
    Check(!await favourites.ToggleAsync(grevId, mario, queueForSync: false), "starring again removes it");
    Check((await favourites.GetAsync(grevId)).Pending.Count == 0, "an unlinked profile queues nothing");

    // The controller editor keeps grev.dad's profile card slot free.
    var slot = new ProfileTile(Guid.Empty.ToString(), ProfileTileKind.Text, 0, 0, 4, 6);
    var slotEditor = new ProfileTileGridEditor([], slot);
    var widgetAdded = slotEditor.AddWidget(ProfileWidgetKind.RecentGames);
    Check(widgetAdded is { Widget: ProfileWidgetKind.RecentGames, Width: 4, Height: 2, X: 4 }, "a new widget avoids the card slot and gets its size");
    Check(ProfileTileGrid.Validate(slotEditor.Tiles) is null, "an added widget is a valid tile");
    slotEditor.HandleInput(InputAction.Left);
    Check(slotEditor.ActiveTile!.X == 4, "a held tile cannot move into the card slot");
    var legacy = new ProfileTileGridEditor([BaseTile(x: 0, y: 0)], slot);
    Check(legacy.Reserved is null, "a layout already using the card cells is not blocked");

    Console.WriteLine("Profile tile tests passed.");
}
finally
{
    Directory.Delete(root, true);
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
