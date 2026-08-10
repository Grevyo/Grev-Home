# Grev Home app model

Milestone 0.3 establishes one generic app model for Library, Store, package installation, launching and future session tracking.

## App definition

An app definition owns stable metadata and launch rules:

- `AppId` — immutable lowercase machine-safe identity, for example `retroarch` or `org.grevhome.pcsx2`.
- `Name` — user-facing app name.
- `Kind` — Application, GameLauncher, Emulator, Utility, Media or SystemTool.
- `InstallStrategy` — where the binary/install belongs.
- `DataStrategy` — where account/configuration data belongs.
- `Launch` — executable, optional arguments, working directory and process name.
- `SupportsController` — whether the app is expected to be directly controller-capable.

UI areas must reference this common definition rather than hard-code individual applications.

## Install strategies

### SharedBinary

One machine-wide binary/install:

```text
Global/Apps/<AppId>/
```

This is the preferred model for large software that can share binaries while keeping user data separate.

### GrevIdPortable

The complete app install belongs to one local Grev Home account:

```text
Profiles/<GrevID>/Apps/<AppId>/
```

This is appropriate for small portable software or apps that cannot safely share one install.

### SystemInstalled

Windows/the application's native installer owns the executable location. Grev Home stores registration metadata under:

```text
Global/Apps/<AppId>/
```

The launch target may point at the external Windows install.

## Data strategies

### GrevId

Each local account gets independent app data:

```text
Profiles/<GrevID>/AppData/<AppId>/
```

This can be combined with `SharedBinary`, giving one emulator/app install with independent user settings/configuration.

### Global

All Grev Home users share the app data:

```text
Global/AppData/<AppId>/
```

### NativeAccount

Grev Home does not redirect the app's data. The external application's own login/account/storage system remains authoritative.

## Important combinations

A typical emulator can use:

```text
InstallStrategy: SharedBinary
DataStrategy:    GrevId
```

Result:

```text
Global/Apps/retroarch/

Profiles/GxxxxGrevxxx/AppData/retroarch/
Profiles/GxxxxAlfiexxx/AppData/retroarch/
```

A small profile-local portable utility can use:

```text
InstallStrategy: GrevIdPortable
DataStrategy:    GrevId
```

A launcher such as a native-account service can eventually use:

```text
InstallStrategy: SystemInstalled
DataStrategy:    NativeAccount
```

## Installed manifests

An installed app is registered by `installed.grevapp.json` inside its Grev Home install/registration root.

The manifest stores:

- the AppDefinition snapshot
- installed version
- install timestamp
- owning GrevID when the binary itself is GrevID-local

The Installed Library scans:

```text
Global/Apps/*/installed.grevapp.json
Profiles/<current-GrevID>/Apps/*/installed.grevapp.json
```

No demonstration/fake installed apps are seeded by Grev Home.

## Catalogue

Known app definitions are persisted at:

```text
Data/Apps/catalog.json
```

The later package/Store system will add or update definitions here when trusted packages are installed or discovered.

## What 0.3 intentionally does not do

- download software
- install packages
- launch applications
- kill applications
- track playtime/processes
- provide Store discovery

Those systems now have a stable app identity/path contract to build on instead of creating their own app-specific storage rules.
