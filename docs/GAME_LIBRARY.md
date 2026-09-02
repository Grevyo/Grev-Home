# Grev Home individual-game library

The game library is a profile-owned content layer beside the installed-app library. A game image
is not registered as another emulator installation and does not create an
`installed.grevapp.json` manifest.

## Ownership and storage

Each persistent GrevID has its own document:

```text
Profiles/<GrevID>/Library/games.json
```

The first schema stores:

- stable game ID;
- display name;
- platform;
- absolute source-file path;
- time added.

Grev Home stores the location only. It does not copy disc images into `C:\GrevHome`, and it never
shares one profile's library tiles with another Primary GrevID.

## First supported platform

The first platform is **PlayStation 2**. Supported file extensions are validated by the platform
definition before an item is written. The UI flow is:

```text
Installed Apps
→ Add Game
→ PlayStation 2
→ Choose Game File
→ profile game tile appears in Installed Apps and on Home
```

The platform selector is deliberately present even while it contains only PlayStation 2. Future
platforms can add their own extension list and emulator resolver without changing existing game
records into fake installed apps.

## Launch resolution

```text
PS2 game tile
→ PlayStation 2 resolver
→ PCSX2 installed for the same Primary GrevID
→ pcsx2-qt.exe -batch -fullscreen -bigpicture "<game path>"
```

The runtime entry borrows the owning GrevID's real PCSX2 binary and data roots. That preserves the
same profile's BIOS, configuration and memory cards while Grev Home displays and records the
individual game name. If PCSX2, the game file or its drive is unavailable, launch stops with a
visible explanation instead of guessing another emulator or path.

The current first slice uses the stable game ID as the runtime activity ID so playtime, Continue,
Recent, recovery and restart resolve the individual title. Before multiple emulators can host the
same content, the runtime schema should make host-app identity and content identity separate
first-class fields rather than overloading either one.

## Integrity rules

- Reads validate the complete document and quarantine malformed data before returning an empty
  library.
- A library written by a newer schema is left untouched.
- Writes use an atomic temporary-file replacement and are serialised so repeated controller input
  cannot race the same profile document.
- Async UI refreshes are tied to the captured Primary GrevID; a late read for the previous player
  is discarded.
- Restart re-resolves the game through its platform and the launch profile's installed PCSX2 rather
  than persisting a fake game manifest.

## Physical test

1. Install and configure PCSX2 for the current GrevID, including that user's own dumped BIOS.
2. Open **Installed Apps → Add Game → PlayStation 2**.
3. Browse to an owned game dump and select it.
4. Confirm the title appears under Games and on Home.
5. Launch it and confirm PCSX2 starts the selected game fullscreen.
6. Confirm Return Home, Overlay, Running Apps, close and restart identify the game title.
7. Exit PCSX2 and confirm Continue/Recent and playtime identify the individual game.
8. Switch Primary User and confirm the first user's game tiles are not visible.
9. Disconnect the game drive and confirm the tile reports the missing file without launching.
