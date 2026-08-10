# Grev Home

Grev Home is a controller-first Windows console shell designed to make a PC usable from boot without requiring a keyboard or mouse. Keyboard and mouse remain fully supported, but every core feature is designed around controller operation first.

## Product rules

- The first screen is always account login / user selection.
- Every persistent local account receives a permanent **GrevID** and permanent **Username** when it is created.
- GrevIDs use the portable format `GxxxxUsernamexxx`, where the four-character prefix and three-character suffix are randomly generated and collision-checked.
- Usernames are limited to **50 characters** and are stored in `profile.json` as immutable account identity data.
- **DisplayName is separate from Username.** It starts equal to Username but may later be changed freely without changing GrevID, Username or folders.
- A true Username change is a last-resort account migration, not a rename: a new account/new GrevID/new filesystem structure will be created and data copied across under an explicit migration flow.
- A session supports multiple signed-in users and controller assignments, with one Primary User selected only for that current session.
- Grev Home uses one persistent fullscreen shell rather than opening separate fullscreen windows for each area.
- Home, Library, Files, Running Apps, App Killer, Settings and future features share one dashboard-style navigation system.
- Local and Guest accounts work without an online dependency. Grev.dad is an optional account / sync / community layer.
- Apps are described by a generic app definition instead of being hard-coded into the UI.
- Grev Home remains resident while launched apps run so controller system shortcuts can restore the shell or open the Grev Overlay.
- Global controller shortcuts are configuration-driven and may be remapped without changing application code.
- Grev Store/package installers are deliberately deferred until the core operating environment is mature.
- Themes/Theme Studio are deliberately late-stage work. Behavior, recovery, system controls, files, storage, accounts and runtime stability come first.
- Normal Grev Home runtime data lives under **`C:\GrevHome`**, which the app creates automatically on first run.

## Identity example

A new local account may start as:

```text
Username:    Grev
DisplayName: Grev
GrevID:      G4P7KGrev9Q2
```

If the user later changes only the DisplayName:

```text
Username:    Grev
DisplayName: Grevyo
GrevID:      G4P7KGrev9Q2
```

The filesystem remains:

```text
Profiles\G4P7KGrev9Q2\
```

Username and GrevID remain fixed. The readable username portion of GrevID is a filesystem-safe snapshot created once when the account is created.

This stable identity is intended to support future **local-profile export/import and sharing**. A transferred account can retain its GrevID, Username and profile data on another Grev Home machine instead of being re-created as a different identity.

## Milestone 0.1

0.1 established the single-window shell, account-first route, shared controller/keyboard navigation, dashboard surface and system-level return-home foundation.

## Milestone 0.2

0.2 turns placeholder identities into real local Grev Home accounts and establishes the multi-user session model:

1. Persistent local accounts with permanent portable GrevIDs.
2. Immutable Username stored with the account.
3. Editable DisplayName separated from Username.
4. Controller-friendly local account creation with an on-screen keyboard.
5. Usernames capped at 50 characters.
6. Per-GrevID storage reserved for Apps, AppData, Saves, Stats, Connections, Screenshots and Themes.
7. Shared machine storage separated into Global Apps / AppData, Packages, Themes, Downloads and Logs.
8. Multiple users can be signed in simultaneously.
9. Controllers 1-4 can be assigned independently to signed-in users.
10. One signed-in user is explicitly the Primary User for the current session only.
11. The Users & Controllers lobby can be reopened without destroying the current session.
12. Guest uses shared guest data without becoming a persistent named local account.

## Milestone 0.3

0.3 establishes the single app catalogue / installed-state model used by Library and the runtime:

1. Stable AppID and generic app definitions.
2. Explicit `SharedBinary`, `GrevIdPortable` and `SystemInstalled` install strategies.
3. Explicit `GrevId`, `Global` and `NativeAccount` data strategies.
4. Shared binary + per-GrevID data is a first-class combination.
5. Installed manifests live with the install they describe.
6. The Installed Apps dashboard reads real manifests and creates no fake/demo entries.

See [`docs/APP_MODEL.md`](docs/APP_MODEL.md).

## Milestone 0.4

0.4 introduces the first real app runtime:

1. Installed apps launch through a central resolver/runtime manager rather than Dashboard click handlers.
2. Grev Home hides while a launched app is foreground but remains resident and continues polling controllers.
3. The runtime tracks the root PID plus descendant processes.
4. A short exit grace period supports starter-process → child-process hand-offs.
5. All users signed in at launch are snapshotted as session participants.
6. The Primary User's GrevID supplies per-account app data for that launch.
7. When the process tree exits, elapsed time is recorded to every participant's stats.
8. Running Apps is a live dashboard surface showing active sessions, elapsed time, participants and process count.
9. Natural app exit restores the Grev Home route that was already active while the shell was hidden.
10. The direct Home action separately returns to Dashboard while leaving the external app running.

See [`docs/RUNTIME.md`](docs/RUNTIME.md).

## Milestone 0.5

0.5 adds the cross-app control layer:

1. Grev Overlay above external apps.
2. Runtime-session App Switcher rather than simulated Alt+Tab.
3. Running Apps switch and graceful-close actions.
4. App Killer with deliberate two-step Force Close.
5. Config-driven global controller shortcut actions.
6. Multiple alternative bindings per action and multi-button/trigger chords.

See [`docs/OVERLAY.md`](docs/OVERLAY.md) and [`docs/CONTROLLER_SHORTCUTS.md`](docs/CONTROLLER_SHORTCUTS.md).

## Milestone 0.6

0.6 begins the Settings/configuration backbone:

1. Settings is an internal dashboard route in the same persistent shell.
2. A local Primary User can edit Display Name without touching Username, GrevID or folders.
3. Username and GrevID are shown as permanent/read-only identity data.
4. The controller shortcut editor reads/writes the same machine-wide config consumed by the runtime.
5. Users can record controller combinations physically instead of editing JSON.
6. Existing combinations can be re-recorded, removed or given a different hold duration.
7. Multiple bindings for the same system action remain supported.
8. Shortcut changes are applied to the running input service immediately.
9. Return Home must always retain at least one enabled controller binding.
10. Settings ownership is split between GrevID-owned and machine-wide configuration rather than becoming one giant settings file.

See [`docs/SETTINGS.md`](docs/SETTINGS.md).

## Runtime data layout

By default Grev Home creates and uses:

```text
C:\GrevHome\
```

For development/testing only, the root can be redirected with `GREV_HOME_ROOT`.

```text
C:\GrevHome\
├── Data\
│   ├── Apps\
│   ├── Runtime\
│   └── Input\
│       └── controller-shortcuts.json
├── Profiles\
│   ├── GxxxxUsernamexxx\
│   │   ├── profile.json
│   │   ├── Apps\
│   │   ├── AppData\
│   │   ├── Saves\
│   │   ├── Stats\
│   │   │   └── playtime.json
│   │   ├── Connections\
│   │   ├── Screenshots\
│   │   └── Themes\
│   └── _GuestShared\
│       ├── AppData\
│       ├── Saves\
│       ├── Stats\
│       │   └── playtime.json
│       └── Connections\
├── Global\
│   ├── Apps\
│   └── AppData\
├── Packages\
├── Themes\
├── Downloads\
└── Logs\
```

`Primary User` never appears in this path model. At launch time the session chooses a signed-in user as Primary, then Grev Home resolves that account's GrevID and uses `Profiles\<GrevID>\...` for account-owned data.

## Username changes

Changing Username will not mutate the existing account. A future migration system will create a new Username, new GrevID and new folder structure, copy eligible account data, validate it, and leave the original account intact until the user explicitly removes or archives it. This is intentionally a last-resort operation.

## Default controls

- D-pad / left stick or arrow keys — move focus
- A or Enter / Space — select
- B or Escape — back
- Return Home ships as **LB + RB + View / 700 ms**
- Grev Overlay ships as **LB + RB + Menu / 450 ms**

The two system combinations above are defaults, not hard-coded behavior. They are editable through Settings and stored in `Data\Input\controller-shortcuts.json`.

## Stack

- Windows 10/11
- .NET 10
- WPF
- XInput as the initial controller backend

## Run

```powershell
dotnet run --project .\src\GrevHome\GrevHome.csproj
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the decisions that future milestones must preserve.
