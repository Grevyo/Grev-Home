# Grev package installer architecture

Grev Home does **not** use one universal installation procedure for all software.

The common package system defines how Grev Home describes, presents, tracks and owns installable content. The actual download/install/update/uninstall workflow is implemented **individually for each package or package family** because different applications distribute and configure themselves differently.

## Core rule

> Generalise the package contract and reusable infrastructure. Do not generalise the software-specific installation workflow.

Examples of differences Grev Home must support include:

- a portable ZIP downloaded from a fixed URL;
- a GitHub release whose current asset URL must be discovered first;
- an EXE bootstrapper;
- an MSI package;
- an application with its own updater;
- a launcher such as Steam/Epic/Riot that installs games separately;
- an emulator that needs GrevID-specific config/save paths after extraction;
- software already installed by Windows that Grev Home only needs to detect/register;
- an application requiring prerequisites or a reboot;
- an application whose install location cannot be freely chosen.

Trying to force all of those through one generic sequence would make packages fragile and would eventually push package-specific exceptions into the generic installer.

## Common package contract

Every package can still expose common metadata to Grev Home:

```text
PackageId
AppId
Name
Version
Type
Install scope
Data scope
InstallerHandlerId
Icon / artwork
Description
Controller support
Capabilities
Installed-version detection
Update availability
```

This is what Store, Library, Downloads and package-management UI consume.

`InstallScope` and `DataScope` remain independent from the installer implementation. A package-specific installer still resolves its destination through the normal Grev Home storage rules:

```text
Global app
→ Global\Apps\<AppId>

GrevID-local app
→ Profiles\<GrevID>\Apps\<AppId>

Shared binary + per-GrevID data
→ Global\Apps\<AppId>
→ Profiles\<GrevID>\AppData\<AppId>
```

## Package-specific installer handlers

The installation engine dispatches the package to a known handler, for example:

```text
Store / Library
      │
      ▼
PackageService
      │
      ├── PCSX2Installer
      ├── RetroArchInstaller
      ├── PlayniteInstaller
      ├── SteamInstaller
      ├── DiscordInstaller
      └── ...one implementation as needed
```

A package-specific handler may implement whichever lifecycle operations make sense:

```text
Detect
Download
Install
ConfigureForGrevId
Verify
Update
Repair
Uninstall
```

Not every package has to support every operation.

For example, a PCSX2 package may download the correct archive, extract a shared binary, create/update profile-specific emulator configuration and verify the executable. Steam may instead run/detect the official installer and then register the native installation without pretending Steam is a portable Grev package.

## Reusable infrastructure

Individual installers should reuse safe low-level services rather than each reimplementing infrastructure. Shared services can include:

- HTTP/download progress and cancellation;
- checksum/hash verification;
- temporary staging directories;
- ZIP/archive extraction;
- safe file copy/move operations;
- process execution and exit-code handling;
- elevation requests where genuinely required;
- installed-version detection helpers;
- registry/query helpers;
- rollback/cleanup primitives;
- package/install logging;
- progress events for controller-friendly UI;
- GrevID/global path resolution;
- restart/reboot requirement reporting.

The shared infrastructure provides tools. **The package handler decides the order and meaning of those tools.**

## Store behavior

The Grev Store therefore does not mean "download this URL and run it". Store installation is:

```text
Select package
      ↓
Resolve that package's installer handler
      ↓
Handler performs its own verified workflow
      ↓
Register resulting InstalledApp manifest
      ↓
Installed Apps / Library
```

The Store should be able to display a consistent progress model even when the package-specific operations differ, such as:

```text
Preparing
Downloading
Verifying
Installing
Configuring
Finalising
Complete
```

A handler can skip phases that do not apply.

## Security boundary

Community/package metadata must not become an unrestricted arbitrary-script execution system.

A manifest may select a trusted/known installer handler and supply validated data that handler explicitly accepts. Package-specific code that can download, execute, write outside Grev Home storage or request elevation remains a trusted Grev Home component and must be reviewed individually.

This still allows a future community ecosystem for safe data-only content such as themes and some portable packages without giving every downloaded manifest arbitrary Windows command execution.

## Build approach

Package support is added **one package at a time**.

For each package:

1. research its current official distribution/update method;
2. decide Global vs GrevID-local binary ownership and data ownership;
3. implement its dedicated handler;
4. implement detection and verification;
5. test install and launch through Grev Home;
6. test update/repair/uninstall where applicable;
7. confirm controller-only user flow;
8. only then publish it in Grev Store.

The first real package should be deliberately straightforward so it proves the dispatch/infrastructure model before a complicated launcher or emulator is attempted.
