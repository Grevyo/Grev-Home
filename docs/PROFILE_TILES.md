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

`SyncProfileTilesAsync` is wired to session start, opening the tile editor, and saving it
(`MainWindow.ProfileTiles.cs`). It has no automated test of its own: exercising it needs a mocked `HttpClient` and
the Windows credential store `WindowsCredentialSecretStore` reads from, which `tests/ProfileTiles`
does not attempt - only `ProfileTileGrid`/`ProfileTileGridEditor`/`ProfileTileService` are covered
there.

### Account isolation on unlink/relink

Unlinking a profile (`GrevDadAccountService.UnlinkAsync`) clears its access credential and link
metadata, but deliberately leaves the local tile layout file alone - there's nothing wrong with the
tiles themselves. If a *different* grev.dad account is then linked to the same local GrevID, that
local layout's `UpdatedAtUtc` belongs to the old account and must never be trusted against the new
one: without a check, a locally-newer-looking stale layout could get pushed and silently overwrite
the new account's real profile tiles with the previous account's leftover content.

`ProfileTileLayout.SyncedAccountUserId` closes this: `SyncProfileTilesAsync` stamps it on every
successful pull or push, and treats a local layout stamped for a *different* account as having no
comparable state at all (as if it were a fresh install) rather than letting its timestamp win.
`ProfileTileService.SaveAsync` preserves it across ordinary local edits (the tile editor's own Save
has no opinion on which account a layout belongs to) and only changes it when a caller explicitly
asks to (`setSyncedAccountUserId: true`), which is currently only `SyncProfileTilesAsync` itself.

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

## Still to do

- **Wiring the editor view into `MainWindow`'s navigation** (see above) and **wiring
  `SyncProfileTilesAsync` to an actual call site** and to an automated test (see Cloud sync above) -
  both exist and are documented but nothing calls either yet.
- `HandleInput` itself still returns `false` for Accept on an empty cell (unchanged) - the view
  adds tiles through its own toolbar (`ProfileTileGridEditor.AddTile`) rather than that path, so a
  D-Pad user reaches "add" via the toolbar buttons (already focus/D-Pad navigable through Grev
  Home's existing `MoveFocus` wiring) rather than pressing Accept on empty space. A more direct
  "Accept on empty cell opens a kind picker right there" flow is still open if that turns out to
  read better once this is actually on screen.
- Editing a tile's picture (`ProfileTileKind.Media`) isn't wired into the view yet - `EditTile_Click`
  shows a message and stops rather than opening a media/file picker.
- Unit/manual verification of everything in this document on an actual Windows/WPF build (a
  `tests/ProfileTiles`-style project exists and covers the non-view pieces; the view itself has no
  test at all - WPF UI is much harder to unit test than the pure logic classes are). This
  repository was authored without access to a .NET toolchain and has not been compiled.
