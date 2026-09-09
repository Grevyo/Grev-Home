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
(`DashboardTileOverride.TileMediaFile`, `ProfilePresentationSettings.BannerImageFile`). A future
sync layer converts between the two representations; this type does not carry a multi-megabyte
string through memory and every JSON round-trip to avoid that conversion.

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

## Still to do

- **A `ProfileTileEditorView`-style XAML surface** that renders the grid and cursor and forwards
  `InputAction`s into `ProfileTileGridEditor`. Nothing in Grev Home actually calls
  `ProfileTileService` or `ProfileTileGridEditor` yet - this branch adds the model and the
  controller-input logic, not a usable feature. This needs a Windows/WPF build to iterate on
  visually, which this change was not made on.
- **A sync layer between `ProfileTileService` (Grev Home) and grev.dad's `/api/profile/tiles`**
  endpoint. None exists. Until it does, a layout edited in Grev Home and one edited on grev.dad are
  two independent copies, not one profile.
- An "add tile" flow from an empty cell (today `HandleInput` deliberately returns `false` for
  Accept on an empty cell so a future catalogue/picker UI can own that instead of the grid editor
  guessing what should appear) - the same boundary grev.dad's own controller draws.
- Unit/manual verification of `ProfileTiles.cs` and `ProfileTileGridEditor.cs` on an actual
  Windows/WPF build (a `tests/ProfileTiles`-style project, matching `tests/ProfileCarousel`). This
  repository was authored without access to a .NET toolchain and has not been compiled.
