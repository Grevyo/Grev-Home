# Grev Home installer

Grev Home ships as a standard Windows Setup .exe built with Inno Setup. Someone installing it downloads one file, runs it, clicks through a normal wizard, and gets a Start Menu entry and (optionally) a desktop shortcut. No .NET runtime install is required separately - the publish step below bundles it.

## Building the installer

Requirements: the .NET SDK, and [Inno Setup 6](https://jrsoftware.org/isdl.php) installed (or its `ISCC.exe` on `PATH`).

```powershell
installer\build-installer.ps1
```

This does two things:

1. `dotnet publish` builds a self-contained `win-x64` copy of Grev Home into `publish\`.
2. Inno Setup (`installer\GrevHome.iss`) packages `publish\` into `dist\GrevHomeSetup.exe`.

`GrevHomeSetup.exe` is the file to hand out - copy it, upload it, whatever. Neither `publish\` nor `dist\` is committed to the repository; both are build output.

## What the installer does

- Installs to `C:\GrevCo\GrevHome` by default (the user can change this during install). `C:\GrevCo` is meant to be a shared home for every piece of Grev software, each in its own subfolder alongside GrevHome - not just this one app.
- Runs without admin elevation (`PrivilegesRequired=lowest`). Installing outside Program Files doesn't need it, and the app itself already creates `C:\GrevHome` (its data root, unrelated to the install folder - see below) without elevation on first run, so this avoids an unnecessary UAC prompt to match.
- Creates a Start Menu group and, if the user opts in, a desktop shortcut.
- Detects a running Grev Home instance (via the same named mutex the app already uses for single-instance enforcement) and asks the user to close it before installing or uninstalling over it, rather than failing partway through.
- Only ever touches files it put under its own install folder. It never touches `C:\GrevHome` (or wherever the user later points Games/BIOS during first-run setup) on install, upgrade, or uninstall - profiles, saves, and installed games/BIOS files are never at risk from running the installer again or removing the app.

Note the two different `C:\Grev...` paths are unrelated: `C:\GrevCo\GrevHome` is where the *installer* puts the app's binaries; `C:\GrevHome` is where the *running app* keeps its data (profiles, saves, etc. - see `AppPaths`). Renaming/moving one has no effect on the other.

## First run

The installer does not ask for Games/BIOS folder locations itself - that happens inside the app, once, the first time it's launched. See `MachineDefaultsService` and `FirstRunSetupView` in `src/GrevHome`. This keeps the installer itself simple and keeps the folder-picking UI controller-navigable like the rest of Grev Home, rather than living in a separate non-controller-friendly wizard.
