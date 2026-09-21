# Grev Home themes

A theme is the shell's chrome palette: ten colors that the shared styles already draw from
everywhere in the app - the base `Button` style, `SharpTileButtonStyle` (every tile),
`ShellModalCardStyle` (every modal card), role badges (Admin/Standard/Guest) and the window
background. A theme does not touch per-app or per-GrevID dashboard tile artwork/colors; those
remain their own override layer as already documented in `DASHBOARD_PRESENTATION.md` (shipped
fallback -> active theme -> GrevID override). Theme work is scoped by the contract in
`ARCHITECTURE.md`'s "Theme contract" section - presentation only, never navigation or process
logic.

## The ten theme colors

`WindowBackground`, `CardBackground`, `CardBorder`, `Surface`, `SurfaceHover`, `Accent`, `Muted`,
`AdminRole`, `StandardRole`, `GuestRole`. Each is a 6-digit hex color. `ThemeDefinition.Validate()`
enforces the format and a 60-character name limit, and is called before any theme is applied or
saved.

## How theming actually re-colors the shell live

The ten colors are Application-level resources (`App.xaml`), and every shared style references
them with `DynamicResource` rather than `StaticResource` specifically so `ThemeApplier.Apply`
can replace them at any time - including while the shell is already running - and have every
element using them re-color immediately, whether it is already on screen or drawn afterward.
`ThemeApplier` never mutates an existing brush's `Color`; each call swaps in a fresh
`SolidColorBrush`, which sidesteps any question of whether a previous brush was frozen.

Every XAML-declared occurrence of the shell's card/section surface (`#11151E` background,
paired with either `#2B3344` or `#3A465F` border - previously two near-duplicate borders, now
both `CardBorderBrush`) and the base button's resting border (`#343D51`) has been swept to these
same `DynamicResource` keys across every view, not just the shared styles - that duplication was
itself a consistency gap independent of theming, and fixing it made both problems disappear
together. Colors declared directly in code-behind (`FindResource(...)` used once to set a
`Background`/`Foreground` at construction time, rather than a live `DynamicResource` binding) do
not re-color until that surface is next rebuilt or reopened. This remaining limit is scoped to
control-level code-behind assignments; there are no more XAML-declared literal duplicates of the
ten theme colors left in the shell's shared chrome.

## Built-in themes

`ThemeCatalog.BuiltIn` ships four: **Grev Default** (the shell's original hardcoded palette,
exactly reproduced so installing this feature changes nothing for a machine that never opens the
Theme Creator), **Twilight Violet**, **Ember** and **Forest**. Built-in themes can never be edited
or deleted; `ThemeService.SaveCustomThemeAsync`/`DeleteCustomThemeAsync` refuse a built-in id
outright. Grev Default is the fallback whenever the active theme id is missing, corrupt, or was
deleted.

## Storage

Themes are machine-wide, not per-GrevID: one signed-in shell has one chrome palette. The active
theme id is a small JSON file alongside shell motion settings
(`Data/Presentation/active-theme.json`). Each custom theme is its own JSON file under the
machine's reserved `Themes` folder, one file per theme - already copyable to another Grev Home
machine by copying the file, though a dedicated in-app import/export flow is not yet built. A
theme file that fails to parse or validate is skipped rather than breaking the whole theme list,
and is left on disk untouched in case it can be repaired.

## Theme Creator

Settings > Theme & Motion > Manage Themes opens the Theme Creator (`Route.ThemeCreator`). It
shows every built-in and custom theme as a gallery tile; selecting one makes it the active theme
immediately. Below the gallery, an editor exposes six of the ten colors for direct editing
(Accent, Surface, Surface Hover, Window Background, Card Background, Muted Text) plus the theme's
name; Card Border and the three role colors carry over unchanged from whichever theme editing
started from, keeping the editor small while every save still produces a fully valid
`ThemeDefinition`.

Editing previews live across the *entire* shell, not just the Theme Creator's own preview panel -
color fields call `ThemeApplier.Apply` on every change. Nothing is persisted until an explicit
save: leaving the Theme Creator without saving re-applies whatever was actually active before it
opened, the same way closing a document without saving discards edits. "New Theme" starts a fresh
unsaved draft from the current edit; "Save Theme" overwrites the custom theme being edited in
place; "Save as New Theme" always mints a new theme; "Delete This Theme" requires pressing twice
(the same two-step shape as App Killer's Force Close) and is hidden for built-ins.

Each of the six editable fields also shows a row of ten quick-pick preset swatches - the same
"click a swatch instead of typing a hex code" convention already used by Dashboard tile artwork,
game tile colors and profile presets elsewhere in the app - so building a theme never requires
knowing a hex code, while an "Enter Hex" button on every field still reaches exact colors through
the existing controller-first `ControllerQwertyKeyboard` overlay (also used for the theme name).
No separate color-picker input surface was introduced.
