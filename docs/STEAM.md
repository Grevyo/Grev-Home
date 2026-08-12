# Steam Integration — Milestone 0.14

Steam is the reference **game-launcher Global App** for Grev Home. It deliberately exercises different platform behavior from Discord: Steam owns a native account, updates itself, launches other processes/games, and is expected to spend most of its normal use in a controller-native Big Picture interface.

## Ownership

- AppId: `steam`
- Kind: `GameLauncher`
- Install strategy: `SystemInstalled`
- Data strategy: `NativeAccount`
- one Windows Steam installation is shared by the machine/user context;
- each persistent GrevID independently chooses whether Steam appears in its Grev Home library;
- removing Steam from one GrevID library does not uninstall Steam or remove games;
- Grev Home never owns or copies Steam credentials, Steam Guard state, game libraries or Steam client data.

## Trusted installer

`SteamInstallerService` is the only trusted Steam package manager registered with Grev Home.

Install behavior:

1. Detect an existing Steam installation through Valve registry values and standard Windows Steam locations.
2. If an existing installation is found, adopt it without reinstalling Steam.
3. Otherwise download Valve's current Windows `SteamSetup.exe` from the Steam CDN into Grev Home staging.
4. Attempt the bootstrapper non-interactively with `/S` under the normal Windows administrator/UAC boundary.
5. Do not trust the bootstrapper exit alone: detect `steam.exe` after it returns.
6. Register Steam only after a real Steam executable is found.
7. Persist the actual detected `steam.exe`/working-directory path, including non-default Steam installations.
8. Delete only temporary Grev Home installer staging afterwards.

`/S` is treated as an attempted silent bootstrap rather than a guaranteed Valve contract. The required safety property is post-install verification: Grev Home must never report Steam installed merely because `SteamSetup.exe` returned.

## Update and repair

Steam owns its native client updater. Grev Home therefore does **not** expose a second Steam Update action.

Repair:

- detects Steam again;
- refreshes a stale/non-default registered launch path when Steam is already healthy;
- can rerun the trusted installation workflow if Steam files are genuinely missing;
- never rewrites Steam account state or game libraries.

## Machine uninstall

Steam intentionally does **not** declare `MachineUninstall` yet.

Normal users can remove Steam from their own GrevID library. Admin Console can inspect/repair Steam but must not show a generic machine-uninstall action.

A future Steam-specific machine uninstall may only be added after Grev Home has an explicit preserve-games/library workflow and a suitably strong destructive warning. Do not route Steam through a generic Global App uninstaller.

## Big Picture launch

The Grev Home launch contract targets:

```text
steam.exe -gamepadui
```

The installer replaces the package's default executable/working directory with the real detected installation path when it registers Steam.

Steam is single-instance. Repeated Grev Home Open requests should reuse/adopt the same managed Steam launcher session rather than creating duplicate Running Apps entries.

## First-run controller setup

Steam ships a per-GrevID `Emulated Keyboard & Mouse` Grev controller profile enabled by default so update/login/Steam Guard/first-run dialogs can be handled from the controller before Big Picture is ready.

Default setup mappings include:

- Right Stick -> mouse cursor
- RT -> left click
- LT -> right click
- Left Stick -> scroll
- X -> Grev Keyboard
- A -> Enter
- B -> Escape
- D-pad -> arrow keys
- LB -> Shift+Tab
- RB -> Tab

The reusable controller guide explains why the layer exists and exposes `Disable Emulated Keyboard & Mouse` as a shortcut to the same per-GrevID App Settings value.

Disabling the Grev profile:

- does not disable Steam's native controller support;
- does not modify Steam Input;
- does not delete Grev mappings;
- remains reversible from Steam App Settings.

Grev Home never requests, stores or auto-fills Steam username/password/Steam Guard credentials.

## Launcher-safe runtime ownership

Steam must not use the ordinary "own the complete child process tree" assumption because Steam launches games.

Steam declares:

```text
ProcessName: steam
AdditionalProcessNames: steamwebhelper
TrackDescendantProcesses: false
ForceKillEntireProcessTree: false
```

This means Grev Home tracks the Steam launcher/UI process groups it explicitly knows about, but does not automatically adopt arbitrary descendants as part of the Steam launcher session.

Force Kill is process-only for the tracked Steam processes rather than recursive process-tree termination. This prevents Grev Home itself from recursively terminating a launched game merely because that game is descended from Steam.

This is a launcher ownership boundary, not a promise that every game can continue independently after the Steam client itself is closed. Grev Home must not claim more than the process scope it controls.

## Shell behavior while games launch

Steam uses:

```text
AppWindowReturnBehavior.KeepShellHidden
```

Big Picture may hide/minimize its own UI while a launched game takes foreground. Grev Home must not interpret that as a request to jump back over the game.

The system Return Home shortcut remains the deliberate way to bring Grev Home back while the launcher session is alive.

## Presentation

Default Steam presentation:

- display name: `Steam`
- tile colour: `#1B2838`
- built-in white Steam glyph

The artwork still flows through the common package defaults -> GrevID presentation override -> Reset to App Default contract.

## Completion boundary

Steam is intended to be the final new application shape added before Grev Home pauses catalogue expansion and finishes the wider shell/backbone.

After Steam is physically validated:

1. freeze the app/package/runtime ownership contracts unless a proven bug requires change;
2. finalise the wider Grev Home UI, profiles, roles/admin, controller navigation, runtime recovery, settings, presentation and core console-shell workflows;
3. only then return to adding additional emulators/apps, which should mostly consist of individual trusted package/installer/configuration work on top of the frozen platform.
