# Grev Home App Platform Backbone — Milestone 0.12+

Milestone 0.12 turned the proven RetroArch Profile App and Discord Global App integrations into a reusable Grev Home application platform. PCSX2 and Steam then pressure-tested that platform with profile-isolated emulator setup and game-launcher process ownership respectively.

The goal is not a generic arbitrary installer scripting system. Every supported application still receives its own trusted installer/manager because download, install, update, repair, configuration and uninstall behavior differs per app. The shared platform standardises **how Grev Home describes and invokes those app-specific workflows**.

## Ownership models

Grev Home currently has two package ownership models.

### Profile App

A Profile App belongs to one persistent GrevID.

RetroArch and PCSX2 are reference implementations:

- binaries are under the owning GrevID;
- mutable app data/saves are GrevID-owned;
- install/update/repair/uninstall are scoped to that GrevID;
- another GrevID does not automatically receive the app;
- uninstall must not affect another profile.

### Global App

A Global App has one Windows/machine installation but independent GrevID library membership.

Discord and Steam are reference implementations:

- the app is installed once for the relevant Windows/machine context;
- each persistent GrevID can include or remove it from its own Grev Home library;
- **Remove from Library is not machine uninstall**;
- adding it back to a GrevID library does not redownload the app when the machine installation still exists;
- machine uninstall is Admin-only **and only exists when that package explicitly declares `MachineUninstall`**;
- native account/update data stays owned by the native app.

Machine installation state and GrevID library membership are deliberately separate pieces of state.

Steam is the explicit example of a Global App which does **not** currently allow machine uninstall in Grev Home because its installed game-library implications require a Steam-specific destructive workflow rather than the generic Global App assumption.

## Lifecycle model

`AppLifecycleState` is the common UI/platform state model:

- `NotInstalled`
- `Installed`
- `RemovedFromLibrary`
- `Running`
- `UpdateAvailable`
- `RepairNeeded`
- `Installing`
- `Updating`
- `Repairing`
- `Uninstalling`

The lifecycle resolver combines:

- registered installed manifest;
- ownership scope;
- current GrevID library membership;
- Grev-managed runtime sessions;
- package version policy;
- package-specific health inspection;
- current in-progress operation.

A machine-installed Global App can therefore be `RemovedFromLibrary` for one GrevID without being `NotInstalled`.

## Capability declarations

Every `GrevStorePackageDefinition` declares the capabilities that its trusted implementation actually supports.

Current `AppPackageCapability` values are:

- `Install`
- `Update`
- `Repair`
- `ProfileUninstall`
- `MachineUninstall`
- `LibraryMembership`
- `ControllerProfile`
- `ControllerGuide`
- `AppSettings`
- `PresentationOverrides`
- `AdminManagement`

Store, App Settings and Admin Console derive available actions from these declarations rather than hardcoding an app name.

Examples:

- RetroArch/PCSX2 declare Install/Update/Repair/ProfileUninstall but not MachineUninstall.
- Discord declares Install/Repair/MachineUninstall/LibraryMembership but not Grev Home Update because Discord owns its native updater.
- Steam declares Install/Repair/LibraryMembership/AdminManagement but deliberately omits both Grev Home Update and MachineUninstall.

## Trusted installer registry

`TrustedPackageInstallerRegistry` maps a package's stable `InstallerId` to one `ITrustedPackageInstaller`.

The common installer contract exposes:

- health inspection;
- install;
- update;
- repair;
- uninstall.

Unsupported operations remain disabled by package capability declarations. The presence of a method on the interface does not mean every package exposes the operation to users.

This removes shell code such as:

```text
if RetroArch ...
else if Discord ...
else if PCSX2 ...
else if Steam ...
```

while preserving individual trusted implementations such as:

```text
RetroArchInstallerService
PCSX2InstallerService
DiscordInstallerService
SteamInstallerService
DolphinInstallerService
```

No downloaded package is allowed to provide arbitrary code/scripts that Grev Home executes as an installer.

## Health, update and repair

Package-specific `InspectAsync` returns a common `PackageHealthSnapshot`:

- `Healthy`
- `RepairRecommended`
- `Unknown`

The Store product page can show health and expose Repair only when the package declares that capability.

### Profile-managed applications

RetroArch/PCSX2 have Grev-owned pinned supported versions. Update/Repair may replace the verified profile-owned binary package while preserving GrevID configuration/data outside the replaceable binary boundary.

Where binary replacement is transactional, the old binary root is moved to a backup, the verified new package is committed, and the backup is restored if commit fails.

### Native-updating Global Apps

Discord and Steam own their native client update lifecycle, so Grev Home does not implement a competing Update button.

Repair validates the native Windows installation and Grev Home registration. If an existing installation has moved/is non-default, the package-specific repair may refresh Grev Home's registered executable path without altering native account data.

## Admin Console boundary

Admin Console is the machine-management surface for Global Apps.

It should show:

- machine-installed version;
- install location;
- current lifecycle/runtime state;
- package health;
- local GrevIDs whose libraries currently include the app;
- trusted Update, Repair and Machine Uninstall actions only when declared.

Every machine-changing operation must re-check Admin role/permission at execution time.

Normal user Store/Installed Apps surfaces never machine-uninstall a Global App.

Machine uninstall is blocked while Grev Home is tracking the target app as running and must use the package's own trusted destructive workflow. A Global App does not gain machine-uninstall merely because it is Admin-managed.

## Runtime policy and process ownership

Runtime behavior has one source of truth for each concern.

`AppLaunchDefinition` owns launch/process behavior such as:

- executable/arguments;
- primary process name used for tracking/adoption;
- optional additional launcher/UI process names;
- `SingleInstance`, consumed by `RuntimeSessionManager` for session reuse;
- whether descendant processes belong to the managed app session;
- whether Force Kill may recursively terminate a tracked process tree.

`AppRuntimePolicy` owns shell/window behavior such as:

- normal versus maximized activation;
- whether Grev Home should return when the app window becomes minimized/hidden.

Do not duplicate `SingleInstance` or process-ownership rules in Store UI logic.

### Ordinary app default

For ordinary apps, older manifests and package definitions retain the established default:

```text
TrackDescendantProcesses = true
ForceKillEntireProcessTree = true
```

This is appropriate when child processes are genuinely part of the app Grev Home owns as one runtime session.

### Launcher-safe ownership

A game launcher can explicitly opt out of that assumption.

Steam is the reference launcher:

```text
ProcessName = steam
AdditionalProcessNames = steamwebhelper
TrackDescendantProcesses = false
ForceKillEntireProcessTree = false
```

Grev Home therefore tracks/adopts the explicitly declared launcher/UI process groups but does not automatically claim every game Steam starts as part of the Steam launcher session. Force Kill is process-only for the tracked launcher processes rather than recursively killing arbitrary descendants.

This boundary prevents Grev Home itself from treating a launched game as owned by the launcher session. It does not promise that a game can necessarily continue if its native launcher is deliberately closed.

Steam also uses `KeepShellHidden` because Big Picture may hide/minimize while a game takes foreground. Grev Home must not jump back over the running game merely because the launcher UI is no longer visible. The system Return Home shortcut remains the deliberate escape path.

The runtime continues to guarantee:

- only Grev-launched/adopted tracked process identities appear in Running Apps/App Killer;
- process identity is PID + start-time validated;
- Switch must activate a real interactive app window, not a hidden utility surface;
- tray/minimized apps can follow package-specific shell return behavior;
- explicit Close can escalate only against the same tracked session;
- Force Kill respects the package/session process-ownership policy;
- system Return Home/Overlay shortcuts keep higher priority than app mappings.

## Reusable onboarding

Packages may declare `AppOnboardingDefinition` containing:

- title;
- summary;
- controller controls to show;
- whether the guide should appear on first launch;
- optional user-facing controller-profile name;
- optional quick action to disable a temporary controller profile after setup.

The guide is rendered inside the existing Grev Overlay architecture, not as another independent WPF application window.

The current controller mappings are resolved at display time, so the guide reflects a GrevID's actual mappings rather than stale hardcoded text.

A persistent GrevID can choose `Don't Show Again`. App Settings exposes `Show Launch Guide Again`, which clears that per-GrevID preference.

PCSX2 and Steam use the same reusable setup pattern: temporary `Emulated Keyboard & Mouse` controls make first-run/login/configuration controller-accessible, then the popup or App Settings can disable only Grev Home's translation while leaving the native application's controller support untouched.

## Presentation contract

Presentation remains independent from install/runtime state.

A package may define defaults for:

- display name;
- tile colour;
- icon;
- tile media;
- hero media.

A persistent GrevID may layer overrides for the same presentation fields. Current storage is under:

```text
Profiles/<GrevID>/Presentation/Apps/<AppId>/
```

`presentation.json` stores metadata such as custom display name/tile colour and the folder may contain validated icon/tile/hero assets.

Reset deletes the GrevID override layer and immediately reveals the package defaults again. It never reinstalls or modifies the app.

Visual assets are presentation data, not theme data. Theme changes must not rewrite them.

Current branded defaults include Discord purple + Discord artwork, PCSX2 dark PS2 blue + the supplied transparent PCSX2 artwork, and Steam dark blue + its built-in Steam glyph.

## Controller profile contract

The existing standard 18-control mapping layout remains the only app-controller mapping system.

Resolution order remains:

```text
package default -> GrevID override -> Reset to package default
```

Native-controller apps can ship a blank/disabled Grev profile. Apps which need temporary first-run desktop input can ship an enabled setup profile and expose a reversible disable helper. System Return Home/Overlay shortcuts remain separate and higher priority.

## Platform freeze / next phase

Steam is intended to be the final new application shape before further catalogue expansion.

After Steam is physically validated:

1. freeze these app/package/runtime ownership contracts unless a proven bug requires a change;
2. finalise the wider Grev Home shell: dashboard/navigation, profiles/roles, Admin Console, Settings, controller UX, runtime recovery, presentation, power/system workflows and remaining backbone;
3. only then resume adding more applications/emulators.

Future emulator/app additions should mostly be individual trusted download/install/update/repair/configuration implementations on top of this platform rather than new shell architecture.

Likely later emulator sequence remains:

1. Dolphin
2. RPCS3
3. PPSSPP
4. DuckStation
5. Cemu
6. Xenia

Do not pre-generalise package-specific installation/configuration details that have not yet been proven by a real package.

See `STEAM.md`, `PCSX2.md`, `RETROARCH.md` and `GREV_STORE.md` for package/surface-specific contracts.
