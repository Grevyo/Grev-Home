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
together. The same duplication turned up again in a second, invisible-to-the-first-guard spelling
- `Color.FromRgb(r, g, b)` instead of a hex string - across another 27 sites; those are unified
too, with the CI guard extended to catch both spellings.

Colors declared directly in code-behind (`FindResource(...)` used once to set a
`Background`/`Foreground` at construction time, rather than a live `DynamicResource` binding) do
not re-color until that surface is next rebuilt. For most surfaces (per-item cards in lists like
Friends, Running Apps, Admin Console, the Grev Overlay's own render methods) that rebuild
constantly during normal use, so this is a non-issue in practice. It does matter for chrome built
once and kept for the app's whole lifetime - the volume/Wi-Fi/Bluetooth quick-control flyouts in
the persistent header, and the Grev.dad account panel on the Profile Edit page - and those now use
`SetResourceReference` instead, which is the code-behind equivalent of `DynamicResource`: it keeps
the property live for as long as the element exists rather than reading the resource once.

## Built-in themes

`ThemeCatalog.BuiltIn` ships four: **Grev Default** (the shell's original hardcoded palette,
exactly reproduced so installing this feature changes nothing for a machine that never opens the
Theme Creator), **Twilight Violet**, **Ember** and **Forest**. Built-in themes can never be edited
or deleted; `ThemeService.SaveCustomThemeAsync`/`DeleteCustomThemeAsync` refuse a built-in id
outright. Grev Default is the fallback whenever the active theme id is missing, corrupt, or was
deleted.

## Storage

Themes have two ownership levels. An Admin controls the machine default, used by Guest and every
GrevID that has not selected an override. Each GrevID can then choose or create a private profile
theme without changing another user's shell. The machine active id lives at
`Data/Presentation/active-theme.json`, while a profile override and its private custom themes live
under `Profiles/<GrevID>/Themes`. Machine custom themes remain under the reserved root `Themes`
folder. Clearing a profile override immediately returns that user to the current machine default.
A theme file that fails to parse or validate is skipped rather than breaking the whole theme list.

## Theme Creator

Settings > Theme & Motion > Manage Themes opens the Theme Creator (`Route.ThemeCreator`). It
shows every built-in and custom theme in the current machine/profile scope as a gallery tile;
selecting one makes it active for that scope immediately. Admins can switch to the machine-default
scope, while normal users can only change their own GrevID. Below the gallery, an editor exposes
six of the ten colors for direct editing
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

## Contrast warnings

`ThemeDefinition.GetContrastWarnings()` checks the color pairs that actually carry text or a
focus ring - button text (a fixed white) against Surface/Surface Hover, Muted text against Window
Background/Card Background, and Accent against Card Background - using the same WCAG relative-
luminance contrast ratio browsers use for accessibility checks. All four built-in themes clear it
comfortably (lowest ratio ~6.5:1 against a 3.0:1 floor).

This is advisory, never a save-blocking rule: a deliberately low-contrast, moody theme is a
legitimate choice, so a low ratio never prevents a save. It shows live as the color fields are
edited (so a problem is visible before you commit to it) and again in the status line after
saving, so it can't be missed by looking away from the warning text at the exact moment of saving.

## Export and import

"Export This Theme" writes whatever is currently being edited - saved or not - to a standalone
`<name>.theme.json` file under the machine's `Downloads` folder (re-exporting the same theme names
a fresh timestamped file rather than silently overwriting the previous export). "Import Theme"
opens a controller-first file browser (`Route.ThemeFilePicker`, mirroring the existing game/photo
file pickers) filtered to `.json` files, defaulting to Downloads; the selected file is validated
and loaded into the editor as a new, unsaved draft - exactly like "New Theme" - so an import can
never silently overwrite an existing saved theme or become active without an explicit Save.

This is theme sharing the same way custom theme storage already was: each theme is one JSON file
copyable between machines by hand. Export/Import just removes needing a file manager to do that
copy.
