# PCSX2 Profile-App Contract — Milestone 0.13

PCSX2 is Grev Home's second emulator integration and the first new Profile App built entirely on the Milestone 0.12 App Platform Backbone.

## App identity

- AppId: `pcsx2`
- PackageId: `pcsx2`
- InstallerId: `pcsx2`
- Kind: `Emulator`
- InstallStrategy: `GrevIdPortable`
- DataStrategy: `GrevId`
- supported Grev Home package version: `2.6.3`

Each persistent GrevID owns an independent PCSX2 installation and PCSX2 data root. Installing PCSX2 for one GrevID must not make it installed for another.

## Official package

Grev Home installs the official PCSX2 Stable Windows x64 Qt portable archive for v2.6.3:

```text
pcsx2-v2.6.3-windows-x64-Qt.7z
```

The package is pinned to the SHA-256 published on the official PCSX2 GitHub release:

```text
963AE6C82BC858A09115C2455247FEB76B453862C04F60D41EF80739D802AE60
```

The trusted installer downloads the archive directly from the official `PCSX2/pcsx2` release, verifies the SHA-256 before extraction, validates archive paths, and extracts without opening an installer wizard.

Grev Home does not execute package-supplied scripts.

## Profile layout

For a profile `<GrevID>`:

```text
C:\GrevHome\Profiles\<GrevID>\
├── Apps\
│   └── pcsx2\
│       ├── pcsx2-qt.exe
│       └── PCSX2 package/runtime files
└── AppData\
    └── pcsx2\
        ├── bios\
        ├── inis / settings created by PCSX2
        ├── memory cards created/configured by PCSX2
        ├── game settings
        ├── covers/cache/resources
        └── other PCSX2 application data
```

The binary root is replaceable package content. The AppData root is persistent GrevID-owned application data.

## PCSX2 data-path integration

Grev Home launches PCSX2 with:

```text
-datapath "{DataRoot}"
```

Current PCSX2 explicitly defines `-datapath <path>` as the directory used for all application data. This lets Grev Home keep PCSX2 binaries separate from mutable profile data instead of relying on a machine-global Documents/AppData location.

PCSX2's current BIOS settings implementation uses `<DataRoot>\bios` as the default BIOS search folder. Grev Home creates that folder when PCSX2 is installed.

## BIOS boundary

PCSX2 requires a PlayStation 2 BIOS to run games. The BIOS is proprietary and Grev Home must never download, bundle, mirror, scrape or otherwise provide BIOS files.

The Store product page displays a dynamic setup notice for the current Primary GrevID:

```text
BIOS required: PCSX2 cannot run games until a PlayStation 2 BIOS dumped from a console you own has been configured. Put the BIOS files in <that GrevID's PCSX2 data folder>\bios, open PCSX2 and select/configure that BIOS. Once this is done, PCSX2 is ready to run your PS2 game dumps.
```

The displayed folder path must resolve from the active GrevID rather than being hard-coded to one Windows account/profile.

A future Grev Home help action may open official PCSX2 BIOS-dumping documentation in the user's browser. That is deliberately outside the initial installer scope.

## Install workflow

Fresh install:

1. require a persistent Primary GrevID;
2. refuse to overwrite a non-empty unverified `Apps\pcsx2` target;
3. download the pinned official PCSX2 archive;
4. verify the published SHA-256;
5. validate archive entries before extraction;
6. extract to temporary staging with no visible setup wizard;
7. verify `pcsx2-qt.exe` exists in a recognised package layout;
8. move verified binaries into `Profiles\<GrevID>\Apps\pcsx2`;
9. create `Profiles\<GrevID>\AppData\pcsx2\bios`;
10. register the installed manifest only after binary/data setup succeeds.

A failed fresh install removes only the newly created PCSX2 binary root. Persistent GrevID AppData is not treated as disposable installer staging.

## Health

The PCSX2 package health check validates:

- profile-owned binary root exists;
- `pcsx2-qt.exe` exists;
- profile-owned PCSX2 data root exists;
- profile-owned `bios` folder exists.

A missing BIOS file does **not** make the package itself damaged. Grev Home reports the installation as healthy while clearly reminding the user that their own dumped BIOS still needs to be placed/configured.

## Update and repair

PCSX2 uses the common 0.12 trusted lifecycle while retaining package-specific implementation.

Update/Repair:

- download and verify the pinned supported PCSX2 package;
- never replace the GrevID AppData root;
- stage new binaries separately;
- move the old binary root to a temporary backup;
- commit the verified new binary root;
- restore the previous binary root if commit fails;
- re-register the supported version only after replacement succeeds.

This allows future Grev Home PCSX2 version bumps without deleting BIOS configuration or other PCSX2 profile data.

## Uninstall

Normal Profile App uninstall removes only:

```text
Profiles\<GrevID>\Apps\pcsx2
```

It intentionally preserves:

```text
Profiles\<GrevID>\AppData\pcsx2
```

including the user's own BIOS files, PCSX2 configuration, memory cards and other application data. Removing that persistent data, if ever offered, must be a separate explicitly destructive action.

Uninstall must never affect another GrevID.

## Controller/runtime behavior

PCSX2 declares native controller support, so its default Grev controller profile remains blank. Grev Home does not translate normal gamepad controls into keyboard/mouse events over PCSX2 by default.

Grev Home still owns the surrounding console-shell behavior:

- controller-first Store/install flow;
- Return Home system shortcut;
- Grev Overlay system shortcut;
- Running Apps;
- App Killer;
- tracked session/playtime;
- Switch/restart/close lifecycle;
- maximized application activation;
- return to Grev Home if the PCSX2 window is minimized/hidden.

System Grev Home shortcuts retain higher priority than app/native controller input.

## Presentation

PCSX2 initially uses the standard neutral Grev Home package artwork placeholder until an approved PCSX2 default artwork asset is added. This does not block installation or runtime work.

Per-GrevID presentation overrides use the same 0.12 presentation contract as RetroArch and Discord.

## Milestone acceptance

Milestone 0.13 is not physically accepted until the target Grev Home Windows machine confirms:

1. PCSX2 appears in Grev Store as a Profile App.
2. The BIOS notice shows the current Primary GrevID's actual PCSX2 data/BIOS path.
3. Download completes using the official verified portable package.
4. `pcsx2-qt.exe` is installed under the current GrevID's `Apps\pcsx2` root.
5. `AppData\pcsx2\bios` is created.
6. Open launches PCSX2 using the GrevID data path.
7. PCSX2 native controller input remains functional.
8. Return Home/Overlay continue to work.
9. Another GrevID does not automatically inherit this PCSX2 install.
10. Repair preserves the AppData/BIOS folder.
11. Profile uninstall removes binaries but preserves AppData/BIOS.

Do not mark physical acceptance from CI alone.
