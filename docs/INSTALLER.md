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

- Installs to `Program Files\Grev Home` (a normal admin-elevated per-machine install, matching the rest of Grev Home's machine-wide design - see below).
- Creates a Start Menu group and, if the user opts in, a desktop shortcut.
- Detects a running Grev Home instance (via the same named mutex the app already uses for single-instance enforcement) and asks the user to close it before installing or uninstalling over it, rather than failing partway through.
- Only ever touches files it put under its own install folder. It never touches `C:\GrevHome` (or wherever the user later points Games/BIOS during first-run setup) on install, upgrade, or uninstall - profiles, saves, and installed games/BIOS files are never at risk from running the installer again or removing the app.

## First run

The installer does not ask for Games/BIOS folder locations itself - that happens inside the app, once, the first time it's launched. See `MachineDefaultsService` and `FirstRunSetupView` in `src/GrevHome`. This keeps the installer itself simple and keeps the folder-picking UI controller-navigable like the rest of Grev Home, rather than living in a separate non-controller-friendly wizard.
