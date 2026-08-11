# Grev Home App Platform Backbone — Milestone 0.12

Milestone 0.12 turns the proven RetroArch Profile App and Discord Global App integrations into a reusable Grev Home application platform before more emulators are added.

The goal is not a generic arbitrary installer scripting system. Every supported application still receives its own trusted installer/manager because download, install, update, repair, configuration and uninstall behavior differs per app. The shared platform standardises **how Grev Home describes and invokes those app-specific workflows**.

## Ownership models

Grev Home currently has two proven package ownership models.

### Profile App

A Profile App belongs to one persistent GrevID.

RetroArch is the reference implementation:

- binaries are under the owning GrevID;
- mutable app data/saves are GrevID-owned;
- install/update/repair/uninstall are scoped to that GrevID;
- another GrevID does not automatically receive the app;
- uninstall must not affect another profile.

### Global App

A Global App has one Windows/machine installation but independent GrevID library membership.

Discord is the reference implementation:

- Discord is installed once for the Windows user/machine context;
- each persistent GrevID can include or remove Discord from its own Grev Home library;
- **Remove from Library is not machine uninstall**;
- adding it back to a GrevID library does not redownload the app when the machine installation still exists;
- machine uninstall is an Admin Console action only;
- native Discord account/update data stays owned by Discord.

Machine installation state and GrevID library membership are deliberately separate pieces of state.

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

Initial `AppPackageCapability` values are:

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

Store, App Settings and Admin Console should derive available actions from these declarations rather than hardcoding an app name.

Examples:

- RetroArch declares Install/Update/Repair/ProfileUninstall but not MachineUninstall.
- Discord declares Install/Repair/MachineUninstall/LibraryMembership but not Grev Home Update because Discord owns its native updater.

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
```

while preserving individual trusted implementations such as:

```text
RetroArchInstallerService
DiscordInstallerService
PCSX2InstallerService
DolphinInstallerService
```

No downloaded package is allowed to provide arbitrary code/scripts that Grev Home executes as an installer.

## Health, update and repair

Package-specific `InspectAsync` returns a common `PackageHealthSnapshot`:

- `Healthy`
- `RepairRecommended`
- `Unknown`

The Store product page can show health and expose Repair only when the package declares that capability.

### RetroArch

RetroArch has a Grev-owned pinned supported version. Update/Repair may replace the verified profile-owned binary package while preserving configuration, saves and states outside the binary root.

The replacement is transactional at the binary-root level: the old binary root is moved to a backup, the verified new package is committed, and the backup is restored if commit fails.

### Discord

Discord owns its native Stable update lifecycle, so Grev Home does not implement a competing Update button.

Discord Repair validates the Windows-user installation and Grev Home registration. If the installation is missing, Repair can re-run the trusted Discord install workflow; if healthy, it refreshes the Grev Home registration.

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

Machine uninstall remains two-stage confirmation and is blocked while Grev Home is tracking the target app as running.

## Runtime policy

Runtime behavior has one source of truth for each concern.

`AppLaunchDefinition` owns process/session launch behavior such as:

- executable/arguments;
- process name used for tracking/adoption;
- `SingleInstance`, consumed by `RuntimeSessionManager` for session reuse.

`AppRuntimePolicy` owns shell/window behavior such as:

- normal versus maximized activation;
- whether Grev Home should return when the app window becomes minimized/hidden.

Do not duplicate `SingleInstance` in the Store runtime policy.

The current runtime continues to guarantee:

- only Grev-launched/adopted tracked processes appear in Running Apps/App Killer;
- process identity is PID + start-time validated;
- Switch must activate a real interactive app window, not a hidden utility surface;
- tray/minimized apps can remain running while Grev Home returns;
- explicit Close can escalate only against the same tracked session;
- Force Kill remains the immediate tracked-process-tree fallback;
- system Return Home/Overlay shortcuts keep higher priority than app mappings.

## Reusable onboarding

Packages may declare `AppOnboardingDefinition` containing:

- title;
- summary;
- controller controls to show;
- whether the guide should appear on first launch.

The guide is rendered inside the existing Grev Overlay architecture, not as another independent WPF application window.

The current controller mappings are resolved at display time, so the guide reflects a GrevID's actual mappings rather than stale hardcoded text.

A persistent GrevID can choose `Don't Show Again`. App Settings exposes `Show Launch Guide Again`, which clears that per-GrevID preference.

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

## Controller profile contract

The existing standard 18-control mapping layout remains the only app-controller mapping system.

Resolution order remains:

```text
package default -> GrevID override -> Reset to package default
```

Native-controller apps can ship a blank/disabled Grev profile. Desktop apps can ship populated mappings. System Return Home/Overlay shortcuts remain separate and higher priority.

## Future package sequence

After this backbone is physically validated, the planned emulator sequence is:

1. PCSX2
2. Dolphin
3. RPCS3
4. PPSSPP
5. DuckStation
6. Cemu
7. Xenia

Each emulator gets an individual trusted package workflow and uses the same lifecycle/capability/Store/runtime/settings/presentation infrastructure.

Do not pre-generalise emulator-specific installation/configuration details that have not yet been proven by a real package.
