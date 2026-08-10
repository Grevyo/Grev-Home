# Grev Home

Grev Home is a controller-first Windows console shell designed to make a PC usable from boot without requiring a keyboard or mouse. Keyboard and mouse remain fully supported, but every core feature is designed around controller operation first.

## Product rules

- The first screen is always account login / user selection.
- A session supports multiple signed-in users and controller assignments, with one primary user owning profile-specific app data while all session participants can later receive playtime.
- Grev Home uses one persistent fullscreen shell rather than opening separate fullscreen windows for each area.
- Home, Library, Store, Files, Running Apps, App Killer, Settings and future features share one dashboard-style navigation system.
- Local and Guest accounts work without an online dependency. Grev.dad is an optional account / sync / community layer.
- Apps are described by a generic app definition instead of being hard-coded into the UI.
- Grev Home remains resident while launched apps run so a controller shortcut can always restore the shell and later open the Grev Overlay / app switcher.
- Grev Store will eventually distribute apps, emulators, themes, add-ons and community packages.
- Themes are portable packages that can be created, exported, imported and eventually shared through Grev Store.

## Milestone 0.1

0.1 established the single-window shell, account-first route, shared controller/keyboard navigation, dashboard surface and system-level return-home shortcut.

## Milestone 0.2

0.2 turns placeholder identities into real local Grev Home profiles and establishes the multi-user session model:

1. Persistent local profiles with permanent GUID identity.
2. Controller-friendly local profile creation with an on-screen keyboard.
3. Per-profile storage reserved for Apps, AppData, Saves, Stats, Connections, Screenshots and Themes.
4. Shared machine storage separated into Global Apps / AppData, Packages, Themes, Downloads and Logs.
5. Multiple users can be signed in simultaneously.
6. Controllers 1-4 can be assigned independently to signed-in users.
7. One signed-in user is explicitly the Primary User for profile-owned apps and data.
8. The Users & Controllers lobby can be reopened without destroying the current session.
9. Guest uses shared guest data without becoming a persistent named local profile.

Actual app installation is intentionally not implemented yet, but the storage contract is now ready for the future package/app system to choose between a profile install and a global/shared install.

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
│   ├── <profile-guid>\
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
