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
- Home, Library, Store, Files, Running Apps, App Killer, Settings and future features share one dashboard-style navigation system.
- Local and Guest accounts work without an online dependency. Grev.dad is an optional account / sync / community layer.
- Apps are described by a generic app definition instead of being hard-coded into the UI.
- Grev Home remains resident while launched apps run so a controller shortcut can always restore the shell and later open the Grev Overlay / app switcher.
- Grev Store will eventually distribute apps, emulators, themes, add-ons and community packages.
- Themes are portable packages that can be created, exported, imported and eventually shared through Grev Store.

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

0.1 established the single-window shell, account-first route, shared controller/keyboard navigation, dashboard surface and system-level return-home shortcut.

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

## Username changes

Changing Username will not mutate the existing account. A future migration system will create a new Username, new GrevID and new folder structure, copy eligible account data, validate it, and leave the original account intact until the user explicitly removes or archives it. This is intentionally a last-resort operation.

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
