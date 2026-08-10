# Grev Home architecture

## Non-negotiable rules

1. **Account first** — Grev Home always enters through Login. Local, Guest and future Grev.dad identities all become session users.
2. **Controller first** — a feature is not complete until its normal flow can be completed without a keyboard or mouse.
3. **One shell** — normal Grev Home navigation occurs inside one persistent fullscreen `MainWindow`. Features must not create replacement fullscreen application windows.
4. **Runtime stays alive** — launching another application must not terminate Grev Home. The runtime owns global controller shortcuts, launch sessions, process tracking, the Overlay and app switching.
5. **GrevID and Username are persistent; Primary User is not** — each local account receives an immutable GrevID and immutable Username when created. Primary User is only a role inside the current signed-in session and never determines folder names.
6. **Display Name is cosmetic** — DisplayName may change without moving folders, changing Username or changing GrevID.
7. **Primary User is separate from participants** — one signed-in user supplies the GrevID-specific environment for a launch while other signed-in participants can independently receive playtime.
8. **Apps are data-driven** — Library, Running Apps, future Store packages and dashboard modules reference common app definitions rather than hard-coded launcher buttons.
9. **Online is additive** — Grev.dad can add sync/community/identity features later; it must not replace or block the local runtime model.
10. **Install scope is explicit** — future software integrations choose GrevID-local or machine-global binary ownership independently from data ownership.
11. **Local identity is portable** — GrevID is safe to use as a folder/export identity so local accounts can later be transferred without depending on a Windows account or machine-specific GUID path.
12. **Launches become runtime sessions** — UI surfaces never independently launch, switch or kill processes. The central runtime owns process identity, participants, elapsed time and exit detection.
13. **System shortcuts are configuration, not code** — Return Home, Overlay and future global controller actions are named actions whose physical button combinations come from validated configuration.
14. **Settings have owners** — GrevID-owned settings stay with the account; machine-wide settings live with the machine/runtime. Do not create one giant undifferentiated settings file.
15. **Backbone before ecosystem/presentation** — system controls, files/storage, runtime recovery, account management and boot/recovery must work before package installers, Grev Store or visual theming are treated as priorities.

## GrevID contract

A local account receives a GrevID exactly once at creation.

Format:

```text
GxxxxUsernamexxx
```

- `G` — Grev identity marker.
- `xxxx` — four randomly generated uppercase alphanumeric characters.
- `Username` — a filesystem-safe readable snapshot of immutable Username at account creation.
- `xxx` — three randomly generated uppercase alphanumeric characters.
- The complete ID is collision-checked against known accounts and existing profile directories before acceptance.
- GrevID matching is case-insensitive because the primary target filesystem is Windows.
- GrevID folder names contain only ASCII letters, digits and `_`.
- Maximum Username length is **50 characters**.
- Maximum GrevID path component length is **58 characters**: `G` + 4 + 50 + 3.
- Whitespace/hyphen/underscore runs in the readable username snapshot become `_`; unsupported characters are omitted. If no safe characters remain, the readable section becomes `User`.
- GrevID never changes during normal account editing.

Example:

```text
Username:    Grev
DisplayName: Grev
GrevID:      G4P7KGrev9Q2
```

If DisplayName later becomes `Grevyo`, Username remains `Grev` and GrevID remains `G4P7KGrev9Q2`.

## Identity model

A persistent local account has three separate fields:

```text
GrevID      permanent portable identity and folder owner
Username    permanent account username stored in profile.json
DisplayName editable cosmetic name shown throughout the UI
```

At account creation, DisplayName initially equals Username. DisplayName can change without changing the other two fields.

A signed-in user additionally receives a temporary **SessionId**. Controller assignments and Primary status refer to SessionId, not folder names.

```text
Local Account
  GrevID:      G4P7KGrev9Q2
  Username:    Grev
  DisplayName: Grevyo

Current Session
  SessionId:   a1...9e
  GrevID:      G4P7KGrev9Q2
  IsPrimary:   true
```

Changing Primary therefore changes which signed-in account's GrevID is used for account-specific launches. It does not rename, move or create profile folders.

## Username change policy

Changing Username is intentionally **not** a normal edit operation.

If a user genuinely needs a different Username, a future account-migration flow will:

1. Validate the requested new Username.
2. Create a completely new local account with a new GrevID based on that Username.
3. Create the new `Profiles/<new-GrevID>/...` structure.
4. Copy the old account's eligible data into the new account structure.
5. Rewrite identity metadata and Grev Home-owned references that must point at the new GrevID.
6. Validate the new account before offering to retire the old one.
7. Keep the original account intact until the user explicitly archives/deletes it.

This remains a last-resort migration rather than a rename.

## Runtime storage contract

The default data root is `%ProgramData%\Grev Home`. `GREV_HOME_ROOT` may override it for development/testing.

A persistent local GrevID owns:

- `Profiles/<GrevID>/Apps` — binaries intentionally installed only for that local account.
- `Profiles/<GrevID>/AppData` — account-specific application configuration/state.
- `Profiles/<GrevID>/Saves` — account-owned save data where Grev Home manages it.
- `Profiles/<GrevID>/Stats` — local playtime/session/statistics data.
- `Profiles/<GrevID>/Connections` — future external-account connection metadata.
- `Profiles/<GrevID>/Screenshots` — user-owned captures.
- `Profiles/<GrevID>/Themes` — reserved account visual-customisation storage for much later work.

Machine-level content uses `Global/Apps` and `Global/AppData`. Guest sessions use `_GuestShared` for shared guest data without creating a durable named identity.

Future software integrations can therefore express combinations such as **shared binary + per-GrevID data**, **GrevID-local binary + per-GrevID data**, or **fully global app**, without changing the identity model.

The portable GrevID also gives future profile export/import a stable identity. If a destination machine already has an imported GrevID, import must treat it as the same identity and use an explicit merge/replace/conflict flow rather than silently minting another ID.

## Session and controller model

- Several users may be signed in simultaneously.
- Controllers 1-4 are assignments to session users, not identities themselves.
- A controller can be reassigned without recreating the user session.
- The first signed-in user becomes Primary by default; Primary can be changed explicitly.
- Primary User is a current-session role only.
- When an app needs account-specific data, Grev Home resolves the Primary User's GrevID and uses `Profiles/<GrevID>/...`.
- Other signed-in users are session participants and can receive playtime independently of whose GrevID owns the launched app context.
- Disconnecting a controller does not destroy its assignment; reconnecting the same XInput slot can resume it.

## App runtime session contract

Every launch through Grev Home creates a `LaunchSession` owned by the runtime layer.

A launch session snapshots:

- LaunchSessionId
- AppID / app name
- Primary GrevID used for the launch context
- signed-in participants at launch time
- start/end time
- root process ID
- discovered child process IDs
- runtime state / failure message

The runtime is responsible for:

1. Resolving the executable safely from the installed app definition.
2. Applying the current Primary User's GrevID to app-data resolution where required.
3. Starting the process.
4. Hiding Grev Home without terminating it.
5. Discovering descendants of known process IDs.
6. Treating the session as active while any tracked process remains alive.
7. Using a short no-process grace period for starter-process → child-process hand-offs.
8. Recording elapsed session duration to every participant's statistics store.
9. Raising state changes consumed by Running Apps, App Killer, Overlay and Switcher.
10. Owning graceful close and force-close process operations.

A natural exit restores the Grev Home route that remained active while the shell was hidden. Explicit Return Home restores Dashboard and leaves the external app/session running.

Crash/reboot recovery for active runtime sessions is a dedicated later backbone milestone.

## Overlay and process-control boundary

Normal Grev Home areas remain inside the one persistent `MainWindow`.

The Grev Overlay is the intentional runtime exception: it is one reusable transparent/topmost native window because it must appear above external applications while the main shell may be hidden. It is not a replacement navigation window and must never recreate the old chain-of-fullscreen-Windows architecture.

Overlay, Running Apps and App Killer all call `RuntimeSessionManager`; none owns a separate process model.

## Controller system shortcut contract

Global controller actions are named system actions. Physical combinations are loaded from:

```text
Data\Input\controller-shortcuts.json
```

Current actions:

```text
ReturnHome
Overlay
```

Current shipped defaults:

```text
ReturnHome → LB + RB + View / 700 ms
Overlay    → LB + RB + Menu / 450 ms
```

These are defaults only. Users may record different combinations, add extra buttons, use triggers, add multiple alternative bindings, change hold time or remove non-essential bindings.

At least one enabled Return Home binding must always exist because it is the controller recovery path. Invalid manually edited configurations fall back to safe defaults for that session.

The runtime input service consumes validated shortcut data and raises actions; views and runtime features must not inspect specific controller-button combinations themselves.

## Settings ownership contract

Settings must live with the component or identity that owns them.

### GrevID-owned

Examples:

- Display Name
- future dashboard/accessibility preferences
- future account connections
- future Grev Home-owned per-user app/emulator preferences

### Machine-wide

Examples:

- controller system shortcuts
- future boot/startup behavior
- future power/system policies
- future Wi-Fi/Bluetooth behavior
- future storage/download destinations
- future runtime recovery settings

Milestone 0.6 establishes Settings as another dashboard route in the permanent shell. It implements local Display Name editing and a controller-first shortcut editor/recorder. See `docs/SETTINGS.md`.

## Package/installer contract — deferred

Package/install support remains architecturally planned but is not a near-term milestone.

The future system will **generalise package metadata and reusable infrastructure, not installation procedures**. Software-specific download/install/update/configuration behavior belongs to trusted per-package/per-family handlers. See `docs/PACKAGE_INSTALLERS.md`.

Package manifests must never become unrestricted arbitrary PowerShell/CMD execution documents.

## Theme contract — intentionally late

Themes remain a product goal, but Theme Engine/Theme Studio work is intentionally deferred until the behavioral/system backbone is stable.

When eventually implemented:

- themes affect presentation/layout only;
- themes never own navigation/business/process logic;
- the built-in default theme is always recoverable;
- theme data should be exportable/importable and eventually shareable.

No current backbone milestone should be blocked on theme abstractions or theme packaging.

## Backbone-first build sequence

Completed/current foundation:

1. **0.1** — persistent shell and controller/keyboard navigation foundation.
2. **0.2** — portable GrevID accounts, immutable usernames, editable display names, multi-user sign-in, controller assignments and session-only Primary User.
3. **0.3** — app catalogue and installed-library model.
4. **0.4** — app launcher, runtime sessions, process-tree tracking and participant playtime.
5. **0.5** — Overlay, app switcher, Running Apps/App Killer close paths and configurable global controller actions.
6. **0.6** — Settings backbone, account presentation editing and controller shortcut recorder/editor.

Next backbone priorities:

7. **0.7 System & Power** — shutdown/restart/sleep, controller/device status, machine/storage status and the system-service abstractions these controls require.
8. **0.8 Files & Storage** — controller-first internal file explorer, drives/USB, safe file operations and storage surfaces.
9. **0.9 Runtime Recovery & Overlay Hardening** — stronger foreground switching, restart-app flow, session recovery/persistence, crash handling and participant/controller runtime controls.
10. **0.10 Dashboard/Data Backbone** — Recently Used, Continue, real stats/cards, notifications/status and download-manager foundations.
11. **0.11 Account/Profile Management** — export/import, safe account retirement, recovery/repair and groundwork for the last-resort Username migration flow.
12. **0.12 Windows Boot/Recovery Integration** — autostart/boot behavior, escape/recovery paths and machine-level operational hardening before any Explorer-shell replacement is considered.
13. **0.13 Account Connections** — secure provider abstraction, Grev.dad integration and RetroAchievements after local/offline behavior is mature.
14. **0.14 Package Infrastructure / Grev Store** — shared download/security/progress plumbing plus package-specific trusted installer handlers added one application at a time.
15. **0.15+ Themes / Theme Studio** — presentation customization only after the core operating experience and ecosystem behavior are established.

Version numbers are roadmap labels, not promises that every item must fit into exactly one PR. The ordering rule matters more than the label: **backbone first, ecosystem second, presentation last.**
