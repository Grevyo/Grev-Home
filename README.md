# Grev Home

Grev Home is a controller-first Windows console shell designed to make a PC usable from boot without requiring a keyboard or mouse. Keyboard and mouse remain fully supported, but every core feature is designed around controller operation first.

## Product rules

- The first screen is always account login / user selection.
- Every persistent local account receives a permanent **GrevID** when it is created. That GrevID owns its filesystem data and does not change with display-name changes or session state.
- GrevIDs use the portable format `GxxxxUsernamexxx`, where the four-character prefix and three-character suffix are randomly generated and collision-checked.
- Local usernames are limited to **50 characters**. The readable username portion of GrevID is filesystem-safe and is captured only when the account is created.
- A session supports multiple signed-in users and controller assignments, with one Primary User selected only for that current session.
- Grev Home uses one persistent fullscreen shell rather than opening separate fullscreen windows for each area.
- Home, Library, Store, Files, Running Apps, App Killer, Settings and future features share one dashboard-style navigation system.
- Local and Guest accounts work without an online dependency. Grev.dad is an optional account / sync / community layer.
- Apps are described by a generic app definition instead of being hard-coded into the UI.
- Grev Home remains resident while launched apps run so a controller shortcut can always restore the shell and later open the Grev Overlay / app switcher.
- Grev Store will eventually distribute apps, emulators, themes, add-ons and community packages.
- Themes are portable packages that can be created, exported, imported and eventually shared through Grev Store.

## GrevID example

A local username `Grev` may receive a GrevID such as:

```text
G4P7KGrev9Q2
```

That ID is permanent. If the display name later changes to `Grevyo`, the GrevID remains `G4P7KGrev9Q2`.

A display name such as `Joe Greeves` can produce a readable GrevID section such as `Joe_Greeves` while the visible display name remains unchanged.

The username part exists to make profile folders easier to identify. Uniqueness comes from the random prefix/suffix plus an actual collision check before creation.

This stable identity is also intended to support future **local-profile export/import and sharing**. A transferred profile can retain its GrevID on another Grev Home machine instead of being re-created as a different identity.

## Milestone 0.1

0.1 established the single-window shell, account-first route, shared controller/keyboard navigation, dashboard surface and system-level return-home shortcut.

## Milestone 0.2

0.2 turns placeholder identities into real local Grev Home accounts and establishes the multi-user session model:

1. Persistent local accounts with permanent portable GrevIDs.
2. Controller-friendly local account creation with an on-screen keyboard.
3. Local usernames capped at 50 characters.
4. Per-GrevID storage reserved for Apps, AppData, Saves, Stats, Connections, Screenshots and Themes.
5. Shared machine storage separated into Global Apps / AppData, Packages, Themes, Downloads and Logs.
6. Multiple users can be signed in simultaneously.
7. Controllers 1-4 can be assigned independently to signed-in users.
8. One signed-in user is explicitly the Primary User for the current session only.
9. The Users & Controllers lobby can be reopened without destroying the current session.
10. Guest uses shared guest data without becoming a persistent named local account.

Actual app installation is intentionally not implemented yet, but the storage contract is ready for the future package/app system to choose between a GrevID-local install and a global/shared install.

## Runtime data layout

By default Grev Home uses the machine-wide Windows data location:

```text
%ProgramData%\Grev Home\
```

For development it can be redirected with `GREV_HOME_ROOT`.

```text
Grev Home\
├── Data\
├── Profiles\
│   ├── GxxxxUsernamexxx\
│   │   ├── profile.json
│   │   ├── Apps\
│   │   ├── AppData\
│   │   ├── Saves\
│   │   ├── Stats\
│   │   ├── Connections\
│   │   ├── Screenshots\
│   │   └── Themes\
│   └── _GuestShared\
│       ├── AppData\
│       ├── Saves\
│       ├── Stats\
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

## Controls

- D-pad / left stick or arrow keys — move focus
- A or Enter / Space — select
- B or Escape — back
- Hold **LB + RB + View** for 700 ms — restore Grev Home and return to Dashboard/Login

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
