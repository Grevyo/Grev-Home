# Profile tile grid

Grev Home's profile tile grid shares its shape and placement rules with grev.dad's web profile
tile editor (`src/profile-card-tiles.ts` and the grid math in `public/profile-tile-save-fix.js` /
`public/dashboard-freeze-fix.js`), so a tile means the same thing wherever it was last edited:

- an 8-column grid, up to 200 rows;
- each tile is 1-6 columns wide, 1-4 rows tall;
- tiles never overlap;
- a link tile always needs a valid `http://` or `https://` URL.

`ProfileCardTiles.cs` (`GrevHome.Profiles`) is the C# side of that contract:

- `ProfileCardTile` / `ProfileCardTileLayout` — the tile shape and a versioned layout.
- `ProfileTileGrid` — pure grid math: `Overlaps`, `InBounds`, `FindFreePlacement`, `Validate`,
  `Compact`. No storage, no UI; both the editor and the persistence service build on this one
  definition of "valid layout".
- `ProfileCardTileService` — per-GrevID JSON storage at
  `Profiles/<GrevID>/Presentation/ProfileTiles/tiles.json`, same atomic
  temp-then-move/schema-version/corrupt-data-quarantine pattern as
  `ProfilePresentationSettingsService`.

Deliberately smaller than grev.dad's tiles for now: no background/media/border styling, just
placement, feature/link/custom kind, and text. That can grow later without changing the placement
contract or the file format (`SchemaVersion` is already in place for it).

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

## Still to do

- A `ProfileTileEditorView`-style XAML surface that renders the grid and cursor and forwards
  `InputAction`s into `ProfileTileGridEditor` — this needs a Windows/WPF build to iterate on
  visually, which this change was not made on.
- An "add tile" flow from an empty cell (today `HandleInput` deliberately returns `false` for
  Accept on an empty cell so a future catalogue/picker UI can own that instead of the grid editor
  guessing what should appear).
- Extending `ProfileCardTile` with the styling grev.dad's tiles have (background, media, borders)
  once the placement contract above has proven out.
- The matching keyboard-driven grid-cursor mode and Gamepad API wiring on grev.dad's own tile
  editor, so a controller works there too and both sides genuinely share one interaction model
  instead of just one file format.
