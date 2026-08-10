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

## Default presentation

The Grev Home RetroArch package ships a default app identity:

- display name: `RetroArch`;
- default tile colour: pure black (`#000000`);
- default artwork: the approved white RetroArch mark supplied for Grev Home;
- artwork is centred and contained rather than cropped;
- the default app tile contains no technical/category text beyond the app name.

Each GrevID may later layer its own display name, icon/artwork, tile colour, tile image/GIF and hero image/GIF over these defaults without editing RetroArch or the package definition. Resetting app presentation reveals the Grev Home defaults again.

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
│       ├── retroarch.exe
│       ├── RetroArch package/runtime files
│       └── retroarch.cfg (initial Grev Home launch configuration)
├── AppData\
│   └── retroarch\
│       ├── remaps\
│       ├── playlists\
│       └── other mutable RetroArch user data as integrations evolve
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

The first installer writes `retroarch.cfg` beside the profile-owned executable because that is the configuration RetroArch naturally consumes on launch. The generated config redirects SaveRAM, states, screenshots, remaps and playlists into GrevID-owned profile folders. A later configuration migration may move the config itself into AppData if the launch contract is changed deliberately; it must never become machine-global.

## Controller-only trusted installation

RetroArch must not open a normal Windows setup wizard from Grev Store.

The trusted installer performs the installation inside Grev Home:

1. require a persistent local Primary GrevID;
2. obtain the official Windows x86_64 portable-package SHA-256;
3. download the pinned supported portable archive over HTTPS;
4. verify the downloaded archive against SHA-256 before extraction;
5. ask Windows' inbox hidden archive tool to list the archive and reject rooted/traversal paths;
6. extract into a temporary staging folder with no visible setup process;
7. verify `retroarch.exe` exists in a recognised package layout;
8. move the verified package into `Profiles/<GrevID>/Apps/retroarch`;
9. create GrevID-owned AppData/SaveRAM/States/Screenshots/remap/playlist directories;
10. write the initial GrevID-specific RetroArch config;
11. register `installed.grevapp.json` only after package/config setup succeeds.

The initial supported package is pinned in `RetroArchInstallerService` rather than following an unversioned `latest` URL. A later Update workflow may move between supported versions explicitly.

### Failure behaviour

- checksum failure installs nothing;
- unsafe archive paths install nothing;
- extraction failure installs nothing;
- missing `retroarch.exe` installs nothing;
- registration happens last;
- a failed fresh install cleans only the new profile's RetroArch binary folder;
- profile AppData/Saves/Connections/Presentation live outside the binary folder and are not treated as disposable package files;
- the installer never touches another GrevID.

The initial extractor is Windows' inbox `tar.exe`/libarchive process launched with `UseShellExecute=false`, `CreateNoWindow=true`, redirected output and no interactive prompts. Physical Windows 11 acceptance testing is authoritative: if the inbox extractor cannot read the verified RetroArch 7z package on the target Windows baseline, extraction must be replaced with another trusted non-interactive implementation rather than falling back to a visible setup wizard.

## Launch ownership

Grev Home launches the RetroArch installation belonging to the **current Primary User's GrevID**.

Example:

```text
Grev is Primary
→ launch RetroArch
→ C:\GrevHome\Profiles\<GrevGrevID>\Apps\retroarch\retroarch.exe

Player 2 becomes Primary
→ launch RetroArch
→ C:\GrevHome\Profiles\<Player2GrevID>\Apps\retroarch\retroarch.exe
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

When the current Primary GrevID owns RetroArch, its Store product page exposes Open and Uninstall instead of Download and Installed Apps exposes the same package artwork/name.

## Package philosophy

RetroArch does not establish a universal arbitrary installer-script system. It is the first implementation of Grev Home's supported-package infrastructure.

Every future supported app receives its own trusted install/update/repair/uninstall workflow because different applications distribute and configure themselves differently.

Every such trusted installer must also be represented in Grev Store through its package definition and receive author-defined default presentation that users can override without changing installer/package behavior.

## Theme boundary

RetroArch/package state, saves, configuration, connections and profile ownership are data/runtime concerns. Themes may change how the installer/library/profile surfaces look, but theme changes must never alter this ownership model.

App-specific icon/tile/hero/colour overrides are profile presentation data and also survive theme changes. The shipped/default theme uses the shared 285 × 145 app-card baseline; future themes may style/layout cards differently without rewriting RetroArch ownership or presentation metadata.
