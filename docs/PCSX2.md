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
│       ├── portable.txt
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

## PCSX2 Stable portable data integration

PCSX2 Stable 2.6.3 does **not** support the later `-datapath` command-line option. Grev Home must not pass that argument to Stable 2.6.3.

Instead, PCSX2 2.6.3's own portable-mode implementation reads `portable.txt` from the application root. If the file contains a relative path, PCSX2 combines that path with its application root and uses the resulting folder as DataRoot.

Grev Home therefore writes a per-install `portable.txt` whose contents resolve from:

```text
Profiles\<GrevID>\Apps\pcsx2
```

to:

```text
Profiles\<GrevID>\AppData\pcsx2
```

The exact relative path is generated with `Path.GetRelativePath`; it is not hard-coded to one Grev Home root or Windows account.

PCSX2's Stable BIOS settings use `<DataRoot>\bios` as the default BIOS search folder. Grev Home creates that folder when PCSX2 is installed or repaired.

## BIOS boundary

PCSX2 requires a PlayStation 2 BIOS to run games. The BIOS is proprietary and Grev Home must never download, bundle, mirror, scrape or otherwise provide BIOS files.

The Store product page displays a dynamic setup notice for the current Primary GrevID:

```text
BIOS required: PCSX2 cannot run games until a PlayStation 2 BIOS dumped from a console you own has been configured. Put the BIOS files in <that GrevID's PCSX2 data folder>\bios, open PCSX2 and select/configure that BIOS. Once this is done, PCSX2 is ready to run your PS2 game dumps.
```

The displayed folder path must resolve from the active GrevID rather than being hard-coded to one Windows account/profile.

A future Grev Home help action may open official PCSX2 BIOS-dumping documentation in the user's browser. That is deliberately outside the initial installer scope.

## Windows prerequisite

PCSX2's supported Windows build requires the Microsoft Visual C++ x64 runtime. Grev Home checks this prerequisite before accepting PCSX2 as healthy. If it is missing, Install/Repair may download Microsoft's official x64 v14 redistributable and invoke the normal Windows UAC/elevation boundary.

## Install workflow

Fresh install:

1. require a persistent Primary GrevID;
2. ensure the supported Windows runtime prerequisite is available;
3. refuse to overwrite a non-empty unverified `Apps\pcsx2` target;
4. download the pinned official PCSX2 archive;
5. verify the published SHA-256;
6. validate archive entries before extraction;
7. extract to temporary staging with no visible PCSX2 setup wizard;
8. verify `pcsx2-qt.exe` exists in a recognised package layout;
9. move verified binaries into `Profiles\<GrevID>\Apps\pcsx2`;
10. create `Profiles\<GrevID>\AppData\pcsx2\bios`;
11. write `Apps\pcsx2\portable.txt` so PCSX2 Stable resolves DataRoot to the GrevID AppData folder;
12. start PCSX2 with its supported `-testconfig` option and require a successful exit;
13. register the installed manifest only after the executable has passed that startup/configuration self-test.

A failed fresh install removes only the newly created PCSX2 binary root. Persistent GrevID AppData is not treated as disposable installer staging.

## Health

The PCSX2 package health check validates:

- profile-owned binary root exists;
- `pcsx2-qt.exe` exists;
- required Microsoft Visual C++ x64 runtime is present;
- profile-owned PCSX2 data root exists;
- profile-owned `bios` folder exists;
- `portable.txt` exists and resolves exactly to the current GrevID's PCSX2 AppData root.

A missing BIOS file does **not** make the package itself damaged. Grev Home reports the installation as healthy while clearly reminding the user that their own dumped BIOS still needs to be placed/configured.

## Update and repair

PCSX2 uses the common 0.12 trusted lifecycle while retaining package-specific implementation.

Update/Repair:

- ensure the Windows runtime prerequisite is available;
- download and verify the pinned supported PCSX2 package;
- never replace the GrevID AppData root;
- stage new binaries separately;
- move the old binary root to a temporary backup;
- commit the verified new binary root;
- regenerate the GrevID `portable.txt` redirect;
- run PCSX2's `-testconfig` startup/configuration self-test;
- restore the previous binary root if replacement or validation fails;
- re-register the supported version only after the new package passes validation.

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
6. `Apps\pcsx2\portable.txt` resolves PCSX2 Stable DataRoot into the current GrevID's AppData folder.
7. PCSX2 passes its `-testconfig` startup validation before install/repair is accepted.
8. Open launches PCSX2 without unsupported command-line options.
9. PCSX2 native controller input remains functional.
10. Return Home/Overlay continue to work.
11. Another GrevID does not automatically inherit this PCSX2 install.
12. Repair preserves the AppData/BIOS folder.
13. Profile uninstall removes binaries but preserves AppData/BIOS.

Do not mark physical acceptance from CI alone.
