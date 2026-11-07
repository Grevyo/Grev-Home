# Profile tile grid

Grev Home's profile tile grid shares its shape and placement rules with grev.dad's own web tile
grid (`public/profile.js` - `profileTileDefaults()`, `PROFILE_COLUMNS`/`PROFILE_MAX_WIDTH`/
`PROFILE_MAX_HEIGHT` - and the placement/collision math duplicated in
`public/profile-tile-controller.js`), so a tile means the same thing wherever it was last edited:

- an 8-column grid, up to 200 rows;
- each tile is 1-6 columns wide, 1-4 rows tall;
- tiles never overlap;
- a link tile always needs a valid `http://` or `https://` URL.

grev.dad actually has **two** separate tile systems, and this is deliberately the grid one, not
the other: `public/profile-card-tiles.js` is a small strip of tiles that lives *inside* the
profile card itself (kinds `feature`/`link`/`custom`, a 4-column x 7-row grid capped at 4 tiles) -
an unrelated feature this does not cover. An earlier version of this file ported that system's
`feature`/`link`/`custom` kind onto this system's grid dimensions, which matched neither side; that
mismatch has been corrected.

`ProfileTiles.cs` (`GrevHome.Profiles`) is the C# side of the grid-tile contract:

- `ProfileTile` / `ProfileTileLayout` — the tile shape (kind `Text`/`Link`/`Media`/`Stat`, matching
  grev.dad's `tileType`, plus its content and styling fields) and a versioned layout.
- `ProfileTileGrid` — pure grid math: `Overlaps`, `InBounds`, `FindFreePlacement`, `Validate`,
  `Compact`. No storage, no UI; both the editor and the persistence service build on this one
  definition of "valid layout".
- `ProfileTileService` — per-GrevID JSON storage at
  `Profiles/<GrevID>/Presentation/ProfileTiles/tiles.json`, same atomic
  temp-then-move/schema-version/corrupt-data-quarantine pattern as
  `ProfilePresentationSettingsService`.

One deliberate divergence: grev.dad stores a tile's background picture as an inline base64 data
URL (like all its other profile media). `ProfileTile.BackgroundMediaFile` instead holds a local
filename, matching how Grev Home stores every other piece of profile/dashboard media
(`DashboardTileOverride.TileMediaFile`, `ProfilePresentationSettings.BannerImageFile`) - this type
does not carry a multi-megabyte string through memory and every JSON round-trip. Also adds
`ProfileTileLayout.UpdatedAtUtc`, which the sync layer below uses to decide which side is newer.

## Cloud sync

`GrevDadProfileSyncService.SyncProfileTilesAsync` (grev.dad side: `docs/profile-tile-sync.md` and
`GET`/`PUT /api/grev-home/profile-tiles` in `grev-home-sync.ts`) is the bidirectional sync path.
Last-write-wins by timestamp: whichever side's tiles were saved more recently is downloaded or
uploaded wholesale (no per-tile merge). A fresh install's local layout has no `UpdatedAtUtc`,
which always loses to a real cloud timestamp - so linking a fresh Grev Home install and calling
this once *is* the profile restore path, not a separate one.

`ProfileTileMediaConverter` (`ProfileTiles.cs`) does the media-representation conversion this
enables: `SaveFromDataUrlAsync` decodes a pulled tile's data URL into a local file (enforcing the
same 1.4 MB limit `ProfileTileService.SaveAsync` already checks), and `ReadAsDataUrl` re-encodes a
local file for push, reusing `ProfileMediaDataUrl.TryReadFile` - the same helper (now split out of
`TryRead`) already used to share a Grev Home avatar/banner as a data URL elsewhere.

`SyncProfileTilesAsync` is a method Grev Home can call, not a job that runs itself - nothing wires
a call site to it yet (e.g. after the tile editor closes, or alongside `SyncAsync`'s own
progression sync). It also has no automated test: exercising it needs a mocked `HttpClient` and
the Windows credential store `WindowsCredentialSecretStore` reads from, which `tests/ProfileTiles`
does not attempt - only `ProfileTileGrid`/`ProfileTileGridEditor`/`ProfileTileService` are covered
there.

## Controller-first editing

A mouse can drag a tile anywhere; a gamepad can't point at anything, so grev.dad's drag-and-drop
model doesn't carry over as-is. `ProfileTileGridEditor.cs` replaces it with a grid cursor built
directly on Grev Home's existing `InputAction` stream (`Up`/`Down`/`Left`/`Right`/`Accept`/`Back` -
the same actions `MainWindow.AppControllerRuntime.cs` already turns D-Pad input into):

```text
Browsing:  D-Pad moves the cursor. Accept on an occupied cell picks that tile up.
Holding:   D-Pad moves the held tile one cell at a time; a move that would overlap another
           tile or leave the grid is rejected and the tile stays put. Accept drops it where
           it is. Back cancels and restores its original position.
Resizing:  (entered from Holding) D-Pad Left/Right change width, Up/Down change height,
           one cell at a time, clamped to 1-6 wide / 1-4 tall. Accept confirms, Back reverts.
```

`ProfileTileGridEditor` is UI-agnostic - it holds no WPF types and draws nothing. A view feeds it
`InputAction`s (`HandleInput` returns `true` when the editor consumed the input, so the caller knows
not to also move page focus) and reads `Tiles`/`CursorX`/`CursorY`/`ActiveTile`/`Mode` to render
itself, the same separation `DashboardTilePresentationService` already has from `DashboardView`.

grev.dad's matching side is `public/profile-tile-controller.js`: a keyboard grid-cursor (arrow
keys/Enter/Escape/R) and a Gamepad API poller, both driving the same up/down/left/right/accept/back
action set as this file, alongside its existing mouse drag.

## The editor view

`Views/ProfileTileEditorView.xaml(.cs)` is the controller-first surface: it renders
`ProfileTileGridEditor`'s tiles and cursor onto a scrollable `Canvas`, and supports everything the
model supports - move, resize, add (a small "add at cursor" toolbar for Text/Link/Media/Stat, using
`ProfileTileGridEditor.AddTile`, new this session alongside `RemoveActiveTile`), remove, and a
minimal text-field edit (Title/LinkUrl/StatValue) via the existing `ControllerQwertyKeyboard`
overlay, the same one `ProfileEditView` already uses for text entry.

It is deliberately self-contained - no dependency on `MainWindow`'s navigation beyond its own
`BackRequested`/`SaveRequested` events - because wiring it into that router is not done here.
`MainWindow.xaml.cs` is large and was not read this session; guessing at its routing conventions
blind risked corrupting working navigation for a much smaller payoff than getting the editor itself
right. Whoever wires this in needs to:

1. Host `ProfileTileEditorView` the way another full-screen surface (e.g. `DashboardTileSettingsView`)
   is hosted - find that pattern in `MainWindow`/`MainWindow.*.cs` first.
2. Call `view.Load(await new ProfileTileService(paths).GetAsync(grevId))` when opening it.
3. Forward the same `InputAction` stream `MainWindow.AppControllerRuntime.cs` already produces for
   D-Pad into `view.HandleInput(action)` while it is the active surface, instead of letting it fall
   through to `MoveFocus` - and set `view.IsControllerActive` from whichever input last arrived
   (controller event vs. a physical `KeyDown`) so the footer prompt reads correctly.
4. On `SaveRequested`, call `ProfileTileService.SaveAsync(grevId, tiles)`; on `BackRequested`,
   return to wherever profile editing was opened from (discarding unsaved changes, or prompting -
   this view raises the event either way and leaves that choice to the host, the same way
   `DashboardTileSettingsView.BackRequested` does).
5. Optionally call `GrevDadProfileSyncService.SyncProfileTilesAsync` after a successful save (see
   Cloud sync below) so a local edit reaches grev.dad promptly rather than waiting for whatever
   other trigger eventually calls it.

**NOT VERIFIED**, same caveat as everything else in this document: authored without a Windows/.NET
toolchain, never compiled. XAML is exactly the kind of thing this environment cannot catch a
mistake in - a bad binding or missing `using` reads fine here and fails only on a real build.

## Live widgets

A tile can carry a widget (`ProfileWidgets.cs`, mirroring `src/profile-widgets.ts` on grev.dad):
recent games, game activity, most played, favourite games, best friends, bio, stats,
achievements and RetroAchievements. A widget tile is a `Text` tile with `Widget` set, plus an
optional `WidgetCount` (1–12 items, list widgets only). That is how grev.dad stores it too, so the
sync wire just adds `widget` and `widgetConfig`. A widget kind this build doesn't know syncs down as
a plain tile.

- **Adding:** in the controller editor, A on an empty cell opens the type picker; widgets follow
  the four plain types. The toolbar's *Live Widget…* button jumps straight to them. A new widget
  takes its natural size where the cursor is, or the first free cell.
- **Settings:** Y on a widget tile shows Title, *Items shown* (Left/Right), then the usual
  appearance fields. Widget tiles have no free text.
- **Card slot:** grev.dad keeps a 4 × 6 slot for the profile card. When the last profile document
  says where it is, the editor draws it and won't let tiles into it, since the website would refuse
  that layout. A layout that already uses those cells isn't blocked.

`ProfileWidgetViews` turns widget data into a `ProfileWidgetView`, which `ProfileTileBoard` draws.
The data comes from grev.dad's resolved widgets (`FromServer`) or, unlinked or offline, from this
PC: local history, milestones, favourites and bio (`FromLocal`).

## Profile pages

- **Your profile** (`ProfileView`): a *Profile space* section draws the tile grid with
  `ProfileTileBoard`. The mini profile card sits in its slot, and *Edit Tiles* opens the editor.
  - Linked: opening the page syncs tiles, favourites and identity, then shows grev.dad's document,
    so it matches the website.
  - Unlinked or offline: it shows the local tiles.
- **A friend's profile** (`FriendProfileView`): their tiles and widgets, as grev.dad resolved
  them for you. Friends see sessions they share with friends, never private ones. It also has
  *Add to best friends*.
- **Controller:** every tile is a focusable button, so the D-Pad moves through a profile tile by
  tile and the page scrolls with it.
  - A on a friend in Best friends opens their profile.
  - A on a link tile opens it in the Grev.dad browser (grev.dad pages) or the general web browser
    (other https pages).
- **Mini profile card** (`ProfileMiniCardView`, and the friend card in `FriendsView`): avatar with
  a presence ring, name, headline, now playing, a short bio, the stats they chose to share, and a
  ★ for best friends. Best friends sort first in the friends list.

## One identity with grev.dad

For a linked profile, the display name, bio, avatar and banner are the same on both sides
(`GrevDadIdentitySyncService`).

- The sync is three-way per field. Each side's fingerprint is compared with the one stored when
  they last agreed (`Presentation/GrevDad/identity-sync.json`), and whichever side changed is copied
  to the other. If both changed, this PC wins.
- The first sync after linking takes grev.dad's value for every field it has. That's how a grev.dad
  profile comes over to a new install.
- Pictures from grev.dad (PNG, JPEG, GIF, WebP) are re-encoded as PNG for the local importers.
- The sync runs after linking, on sign-in, after saving Edit Profile and when your profile page opens.
- Bios are now up to 800 characters with line breaks, matching grev.dad.
- Saving the public card sends only its display options. Identity goes only through the sync, so
  saving on this PC can't overwrite a picture or bio changed on the website.

## Favourite games and RetroAchievements

- **Favourite games:** each game's settings page has a ☆ button. Favourites work offline
  (`FavouriteGameService`). For a linked profile, changes queue and replay to grev.dad, then
  grev.dad's list becomes the local list, so games starred on the website show up here too.
- **RetroAchievements:** Edit Profile → Grev.dad account has a RetroAchievements section. Enter
  only your RetroAchievements username; grev.dad looks the account up with its own API key. No
  RetroAchievements password or key is ever entered in Grev Home.

## Tests

`tests/ProfileTiles` covers:

- the widget contract against grev.dad's list, and widget validation
- the sync wire round trip, including unknown kinds
- `FromServer` with every widget shape, plus a real profile document captured from a local
  grev.dad worker (`profile-document.json`)
- the offline views
- identity sync decisions
- the favourites queue
- the editor's card slot

It runs on Linux with `dotnet exec`. The WPF views (`ProfileTileBoard`, `ProfileMiniCardView`, the
page changes) compile, but they need checking on Windows.
