# Grev Store & App Presentation

Grev Store is the discovery surface for every application that Grev Home officially knows how to download/install.

## One package definition, one source of truth

A supported package is represented by `GrevStorePackageDefinition`.

The package definition owns:

- stable PackageId;
- stable InstallerId;
- Store category;
- technical `AppDefinition`;
- install/data scope;
- controller support;
- Store description;
- Grev Home integration descriptions;
- Grev Home default presentation.

The Store is populated from these trusted package definitions. A supported installer must not be implemented as a hidden/manual workflow that is missing from Grev Store.

Adding App #2 later means adding its trusted installer/package definition; that same definition becomes its Store entry and product-page information.

## Store categories

The initial category contract is:

- Gaming;
- Emulators;
- Apps;
- Media;
- Tools.

Categories are presentation/discovery metadata. They do not determine install ownership.

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
- installed state, version, install date and owner where available.

Installed state is derived from the existing `installed.grevapp.json` manifest model rather than a second Store-specific downloaded flag.

### Action state

If the package is not installed for the relevant scope, the primary action is:

```text
Download
```

For a Profile App, Download is disabled unless a persistent local Primary GrevID exists.

If the exact package is already installed for the relevant scope, the product page instead exposes:

```text
Open
Uninstall
```

`Open` launches the existing installed manifest through Grev Home's central runtime/session manager.

`Download` and `Uninstall` are package-installer operations. They must delegate to the trusted installer identified by the package's `InstallerId`; the Store page must never implement unsafe generic download/extract/delete logic itself.

This allows the product-page UX and installed-state model to be validated before RetroArch's actual downloader/install/uninstall implementation is added.

## Default app-tile presentation

Every supported package has an author-defined default presentation that works for every Grev Home user.

The default app tile is intentionally simple:

- one package-supplied app icon/artwork image;
- the app display name;
- no category, install scope, description, version, initials, abbreviations or other text inside the tile.

The Store/product-list surfaces must never use text such as `RA`, `APP`, `PCSX2` or similar initials as substitute artwork. The old fallback-glyph concept is removed from the package presentation model.

If a package image has not yet been supplied during development, Grev Home uses one neutral graphical placeholder containing no text. Supplying the package's real default `IconAsset` replaces that placeholder automatically on all surfaces that use the standard app-artwork presenter.

All richer information belongs on the product/details page rather than being squeezed into the app tile.

Each package may also supply:

- default tile media;
- default hero media.

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

Supported custom visual formats are PNG, JPG, JPEG, BMP and GIF, with a 25 MB per-file limit in the initial contract.

The override may replace:

- app display name;
- icon;
- tile media;
- hero media.

An unset override always falls back to the Grev Home package default. Resetting presentation removes the override layer rather than modifying/reinstalling the application.

The future customisation UI should only expose these controls for an installed app. The underlying presentation storage remains separate from installer/runtime state.

Custom app presentation is profile data, not theme data. Changing themes must not delete or rewrite app presentation overrides.

## Animated media

GIF is accepted as app presentation media in the storage/validation contract. The presentation layer must treat animated media as presentation only; it cannot alter package/runtime behavior.

A dedicated animated-media renderer can evolve independently from installer/package storage. If animation is unavailable on a surface, the app must still have a deterministic static/fallback presentation rather than becoming unusable.

## Default theme app-card size

The shipped/default Grev Home theme uses one canonical application tile size:

```text
285 × 145
```

Dashboard application tiles, Grev Store package tiles and Installed Library app tiles should use this baseline size in the default theme.

Artwork is rendered inside the standard card rather than changing the card's layout dimensions.

Future themes may expose different presentation/layout rules, but package definitions and user app-presentation data remain independent from those theme choices.

## Installer scope is separate from presentation

Store category, icon, tile GIF and hero media never decide where an app installs.

Install ownership remains controlled by `InstallStrategy` and `DataStrategy`.

For RetroArch:

```text
InstallStrategy = GrevIdPortable
DataStrategy    = GrevId
```

so its Store card is an Emulator/Profile App and its binary/data ownership remains isolated to the selected GrevID regardless of how the user customises its visuals.

## Current first package

RetroArch (`retroarch`) is the first registered Grev Store package and first trusted app-specific installer target.

Its product page explains its multi-system emulation purpose, Profile App isolation, Grev Home runtime/playtime integration and planned profile-owned RetroAchievements integration.

The package catalogue/discovery/product-page/presentation contract is established before its downloader/install execution is added, so the installer can concentrate on safe download, extraction, profile configuration, update, repair and uninstall behavior.
