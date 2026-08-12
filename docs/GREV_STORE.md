# Grev Store & App Presentation

Grev Store is the discovery and management surface for every application that Grev Home officially supports through a trusted package definition.

The wider lifecycle/capability/runtime contract is documented in `docs/APP_PLATFORM.md`. This document concentrates on Store discovery, product-page behavior and presentation.

## One package definition, one source of truth

A supported package is represented by `GrevStorePackageDefinition`.

The package definition owns:

- stable PackageId;
- stable InstallerId;
- Store category;
- technical `AppDefinition`;
- install/data scope;
- declared package capabilities;
- controller defaults where applicable;
- runtime/window policy;
- version/update policy;
- reusable onboarding metadata where applicable;
- Store description;
- Grev Home integration descriptions;
- Grev Home default presentation.

The Store is populated from these trusted package definitions. A supported installer must not be implemented as a hidden/manual workflow that is missing from Grev Store.

Package-specific install/update/repair/uninstall code remains individual. The shared trusted installer registry only routes Grev Home to the implementation declared by the package's `InstallerId`; it is not an arbitrary script system.

## Store categories

The category contract is:

- Gaming;
- Emulators;
- Apps;
- Media;
- Tools.

Categories are presentation/discovery metadata. They do not determine install ownership.

## Profile Apps and Global Apps

Install ownership is separate from Store category and presentation.

### Profile App

A Profile App is installed for one persistent GrevID. RetroArch is the reference implementation.

For RetroArch:

```text
InstallStrategy = GrevIdPortable
DataStrategy    = GrevId
```

Its binaries/data ownership and install/update/repair/uninstall operations remain scoped to the selected GrevID.

### Global App

A Global App has one Windows/machine installation while each persistent GrevID has independent Grev Home library membership. Discord is the reference implementation.

For a Global App:

- `Installed on machine` and `in this GrevID's library` are different states;
- `Remove from Library` never uninstalls the Windows application;
- `Add to Library` restores access to an existing machine installation without downloading it again;
- machine uninstall is restricted to Admin Console and a trusted package-specific uninstaller.

## Store product page

Selecting an app/package in the Grev Store grid opens a dedicated internal product page inside the permanent Grev Home `MainWindow`.

The grid is discovery only; selecting a card must not immediately install software.

The product page shows:

- app name and default presentation;
- Store category and app kind;
- Profile App versus Global App ownership;
- per-profile/global/native app-data behavior;
- controller-support declaration;
- what the application does;
- how the application integrates with Grev Home;
- expected/current install location;
- lifecycle state;
- installed version/date/owner where available;
- package health and repair recommendation where supported.

Installed state is derived from the existing `installed.grevapp.json` manifest model plus library membership/runtime/package health. There is no second Store-only downloaded flag.

## Lifecycle and action state

The common lifecycle includes:

- Not Installed;
- Installed;
- Removed from Library;
- Running;
- Update Available;
- Repair Needed;
- Installing;
- Updating;
- Repairing;
- Uninstalling.

The product page exposes only actions declared by that package and valid for the resolved lifecycle. Possible actions include:

```text
Download
Add to Library
Open
Update
Repair
App Settings
Uninstall
Remove from Library
```

Examples:

- a missing Profile App may show `Download`;
- a machine-installed Global App removed from this GrevID may show `Add to Library`;
- an installed package may show `Open` and `App Settings`;
- a Grev-owned versioned package may show `Update` when its installed manifest differs from the declared supported version;
- a package with a failed health check may show `Repair`;
- a Profile App may show `Uninstall` when its trusted package declares profile uninstall;
- a Global App may show `Remove from Library`, while machine uninstall remains Admin Console only.

For a Profile App, install/update/repair/uninstall require the correct persistent Primary GrevID. Update/repair/uninstall are blocked while Grev Home is tracking that package as running.

`Open` launches the existing installed manifest through Grev Home's central runtime/session manager.

All package-changing actions delegate to the trusted installer registry. The Store page must never implement generic unsafe download/extract/delete behavior itself.

## Current trusted package examples

### RetroArch

RetroArch (`retroarch`) is the reference Profile App.

It currently demonstrates:

- profile-owned binaries/data;
- pinned trusted version and checksum;
- install;
- package health inspection;
- transactional binary update/repair that preserves GrevID data;
- profile-scoped uninstall;
- native controller use with a blank Grev controller profile.

### Discord

Discord (`discord`) is the reference Global App.

It currently demonstrates:

- one Windows-user installation;
- per-GrevID Grev Home library membership;
- native Discord account/update ownership;
- trusted install/registration;
- package health inspection and repair;
- Admin-only machine uninstall;
- single-instance runtime adoption;
- Grev desktop-controller defaults;
- reusable controller onboarding;
- package-defined maximized launch behavior.

Discord intentionally does not expose a Grev Home `Update` action because Discord owns its native Stable updater.

## Default app-tile presentation

Every supported package has an author-defined default presentation that works for every Grev Home user.

The default app tile is intentionally simple:

- package-supplied app icon/artwork where available;
- package-defined tile colour;
- app display name;
- no category, install scope, description, version, initials, abbreviations or other technical text inside the tile.

The Store/product-list surfaces must never use text such as `RA`, `APP`, `PCSX2` or similar initials as substitute artwork. If a package image has not yet been supplied, Grev Home uses one neutral graphical placeholder containing no text.

All richer information belongs on the product/details page rather than being squeezed into the app tile.

Each package may define defaults for:

- display name;
- tile colour;
- icon;
- tile media;
- hero media.

Package defaults are owned by Grev Home/package authors and are safe to restore at any time.

## User presentation overrides

After an app is installed, a persistent GrevID may customise its presentation without modifying the package definition or installed app files.

Overrides live under:

```text
C:\GrevHome\Profiles\<GrevID>\Presentation\Apps\<AppId>\
├── presentation.json
├── icon.<ext>
├── tile.<ext>
└── hero.<ext>
```

Supported custom visual formats are PNG, JPG, JPEG, BMP and GIF, with a 25 MB per-file limit in the current contract.

The override layer may replace:

- app display name;
- tile colour;
- icon;
- tile media;
- hero media.

Resolution is always:

```text
package default -> GrevID override
```

An unset override falls back to the package default. `Reset Appearance to App Default` deletes the GrevID override layer and immediately reveals the package presentation again; it does not reinstall or modify the application.

Custom app presentation is profile data, not theme data. Changing themes must not delete or rewrite app presentation overrides.

## Animated media

GIF is accepted as app presentation media in the storage/validation contract. Animated media is presentation only and cannot alter package/runtime behavior.

A dedicated animated-media renderer can evolve independently. If animation is unavailable on a surface, the app must still have deterministic static/fallback presentation rather than becoming unusable.

## Default theme app-card size

The shipped/default Grev Home theme uses one canonical application tile size:

```text
285 × 145
```

Grev Store and Installed Apps use this baseline in the default theme. Artwork is rendered inside the standard card rather than changing card dimensions.

Future themes may expose different visual/layout rules, but package definitions, ownership, lifecycle and per-GrevID presentation remain independent from theme choices.

## App Settings boundary

Presentation, controller settings and onboarding preferences are profile-facing settings and remain separate from installer/runtime state.

For packages that declare the relevant capabilities, App Settings can show:

- package/onboarding information and `Show Launch Guide Again`;
- presentation source/current defaults and `Reset Appearance to App Default`;
- the standardized per-GrevID controller-profile editor/reset.

These reset operations are independent: resetting the controller profile does not reset presentation; resetting onboarding does not reinstall the app; resetting presentation does not alter controller mappings.

## Admin Console boundary

Normal Store surfaces do not machine-uninstall Global Apps.

Admin Console is the machine-management surface and may expose, when declared by the package:

- machine version/location;
- runtime/lifecycle state;
- package health;
- GrevIDs whose libraries include the app;
- trusted machine Update;
- trusted machine Repair;
- trusted machine Uninstall.

Admin permission is checked again when the operation executes, and machine uninstall remains a confirmed destructive action.

## Package philosophy

Grev Home does not provide an arbitrary installer scripting engine.

Every supported app gets an individual trusted package implementation because distribution, configuration and lifecycle behavior differ. Shared Store/lifecycle/capability/registry infrastructure exists so adding future packages such as PCSX2 or Dolphin does not require hardcoding app-name branches throughout the shell.

See `docs/APP_PLATFORM.md` for the complete Milestone 0.12 backbone contract and `docs/RETROARCH.md` for the Profile App reference implementation.
