# RetroArch Profile-App Contract

RetroArch is the first real Grev Home package and is deliberately a **Profile App**, not a Global App.

## App identity

- AppId: `retroarch`
- Kind: `Emulator`
- InstallStrategy: `GrevIdPortable`
- DataStrategy: `GrevId`
- Every installed RetroArch manifest must have an `OwnerGrevId`.

Installing RetroArch for one GrevID does not install it for any other GrevID.

## Grev Store identity

RetroArch is also the first registered trusted Grev Store package:

- PackageId: `retroarch`
- InstallerId: `retroarch`
- Store category: `Emulator`
- install scope: Profile App

The same package definition supplies both Store discovery metadata and the technical `AppDefinition`. There must not be a second manually maintained Store entry that can drift away from the installer definition.

The Grev Home package supplies default presentation that works on a clean profile. Each GrevID may later layer its own app display name, icon, tile media/GIF and hero media over those defaults without editing the package or RetroArch files. Resetting presentation reveals the Grev Home defaults again.

See `docs/GREV_STORE.md` for the general Store/presentation contract.

## Why RetroArch is profile-owned

Each Grev Home user must be able to maintain an independent RetroArch environment without conflicts with another local user. That includes:

- RetroAchievements identity/login;
- RetroArch configuration;
- cores and per-core settings where applicable;
- remaps/controller configuration;
- playlists/history/favourites;
- save RAM;
- save states;
- screenshots;
- user-specific emulator preferences;
- future profile-facing RetroAchievements/stat data.

No RetroArch configuration or RetroAchievements account may silently leak between GrevIDs.

## Profile layout

For a profile `<GrevID>`, the intended ownership is:

```text
C:\GrevHome\Profiles\<GrevID>\
├── Apps\
│   └── retroarch\
│       ├── RetroArch binaries
│       ├── cores
│       └── package-owned runtime assets
├── AppData\
│   └── retroarch\
│       ├── retroarch.cfg
│       ├── per-core/per-game configuration
│       ├── remaps
│       ├── playlists/history/favourites
│       └── other mutable RetroArch user configuration
├── Saves\
│   └── retroarch\
│       ├── SaveRAM\
│       └── States\
├── Screenshots\
│   └── RetroArch\
├── Connections\
│   └── RetroAchievements\
│       └── profile-owned connection metadata
└── Presentation\
    └── Apps\
        └── retroarch\
            ├── presentation.json
            ├── icon.*
            ├── tile.*
            └── hero.*
```

Exact RetroArch directory names/config directives are installer-specific and must be validated against the supported Windows RetroArch package before implementation. The ownership boundaries above are the Grev Home contract.

## Launch ownership

Grev Home launches the RetroArch installation belonging to the **current Primary User's GrevID**.

Example:

```text
Grev is Primary
→ launch RetroArch
→ C:\GrevHome\Profiles\<GrevGrevID>\Apps\retroarch\...

Player 2 becomes Primary
→ launch RetroArch
→ C:\GrevHome\Profiles\<Player2GrevID>\Apps\retroarch\...
```

Changing Primary affects future launches only; it never mutates a RetroArch instance that is already running.

Other signed-in users may still be recorded as launch participants by Grev Home, but the running RetroArch process/configuration belongs to the Primary user's profile. RetroAchievements credentials must never be combined across participants.

## RetroAchievements ownership

RetroAchievements is a **GrevID/profile connection**, not a machine-global connection.

RetroArch is the first consumer of that connection. Future supported emulators may use the same GrevID's RetroAchievements identity without creating a second machine-global account or reading another user's credentials.

Connection secrets must not be placed in global Grev Home configuration or another profile's folder. The eventual credential-storage mechanism must be designed separately from public profile metadata.

## Install, update, repair and uninstall safety

All package operations are scoped to one explicit GrevID.

- Install creates/updates only that GrevID's RetroArch app/data structure.
- Update replaces package-owned binaries/assets without deleting saves or profile-owned configuration.
- Repair restores required package-owned files and validates configured profile paths without resetting saves by default.
- Uninstall removes the selected GrevID's RetroArch binary install only by default.
- Deleting configuration/saves is a separate destructive action and must require explicit confirmation.
- Operations must never traverse into another GrevID's profile tree.

## Library semantics

RetroArch is considered installed only for the GrevID that owns its manifest. If Grev has RetroArch installed and Player 2 does not, the UI must not claim Player 2 has RetroArch installed.

A future app/library view may offer **Install for this profile** when the current Primary User does not yet own the package.

## Package philosophy

RetroArch does not establish a universal arbitrary installer-script system. It is the first implementation of Grev Home's supported-package infrastructure.

Every future supported app receives its own trusted install/update/repair/uninstall workflow because different applications distribute and configure themselves differently.

Every such trusted installer must also be represented in Grev Store through its package definition and receive author-defined default presentation that users can override without changing installer/package behavior.

## Theme boundary

RetroArch/package state, saves, configuration, connections and profile ownership are data/runtime concerns. Themes may change how the installer/library/profile surfaces look, but theme changes must never alter this ownership model.

App-specific icon/tile/hero overrides are profile presentation data and also survive theme changes. The shipped/default theme uses the shared 285 × 145 app-card baseline; future themes may style/layout cards differently without rewriting RetroArch ownership or presentation metadata.
