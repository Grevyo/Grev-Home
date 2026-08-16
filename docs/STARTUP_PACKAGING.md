# Grev Home startup and packaging contract

Grev Home's first appliance release remains a normal Windows desktop application that starts automatically after Windows sign-in. It does **not** replace Explorer as the Windows shell.

## Runtime ownership

Only one Grev Home process may own the console shell/runtime state in a Windows session. A second launch signals the existing instance to surface itself and then exits.

Controller polling starts only after the MainWindow integration graph is wired, machine/profile state is loaded, and the initial Login route exists.

## Windows startup

A published/installed `GrevHome.exe` registers itself under the current user's Windows Run key. Development executables under build output or temporary folders never register themselves.

The default installed application path is:

`%LOCALAPPDATA%\Programs\GrevHome`

The startup executable path is therefore an installation concern. Grev Home user data is not stored beside the executable.

## Crash recovery

Grev Home writes a shell-session marker under `C:\GrevHome\Data\Runtime`. A normal exit removes it. If the next launch finds the marker, the previous shell did not record a clean shutdown and startup continues through the normal runtime recovery path.

Published/installed builds may automatically relaunch after a fatal shell crash. Automatic relaunch is bounded to two consecutive recovery attempts so a broken build cannot enter an uncontrolled restart loop. Development builds log fatal failures but do not auto-relaunch.

Runtime application recovery remains owned by `RuntimeSessionManager`: persisted process identities are revalidated, stale records are dropped, overlapping recovered sessions are deduplicated, and playtime is never fabricated for an app that ended while Grev Home was not running.

## Sleep and resume

The existing MainWindow remains the console shell across Windows sleep/resume. On resume Grev Home refreshes controller configuration and runtime/session surfaces and reasserts borderless maximized presentation if MainWindow is visible.

If a tracked external app currently owns the console surface and MainWindow is deliberately hidden, resume does not force Grev Home over that app.

## Publish output

The retained Windows publish profile is:

`src/GrevHome/Properties/PublishProfiles/win-x64.pubxml`

It publishes:

- Release configuration
- Windows x64
- self-contained .NET runtime
- normal multi-file WPF output
- no ReadyToRun requirement
- no debug symbols in the release publish

The application version is declared in `GrevHome.csproj`.

`tools/Publish-GrevHome.ps1` creates the release payload under `artifacts\GrevHome-win-x64` by default and copies the install/uninstall scripts into that payload. CI parses all release PowerShell and performs a real self-contained publish so packaging failures cannot hide behind a successful normal build.

## Install and upgrade

`tools/Install-GrevHome.ps1` is the first release installer/upgrader. A published release payload can therefore install without modifying the Grev Home data root.

The installer:

- validates that the source payload contains `GrevHome.exe`
- stages the full new payload beside the installed application directory
- stops only a running Grev Home process whose executable path matches the installed target
- keeps one previous application version in `%LOCALAPPDATA%\Programs\GrevHome.previous`
- swaps the staged application directory into `%LOCALAPPDATA%\Programs\GrevHome`
- restores the previous application directory if the swap fails
- registers Grev Home in the current-user Windows Run key
- creates a Start Menu shortcut
- launches the new build unless `-NoLaunch` is requested

No profile, save, app-data or runtime-state directory is copied into the application installation.

## Uninstall

`tools/Uninstall-GrevHome.ps1` removes the application installation, rollback copy, Run-key entry and Start Menu shortcut.

By default it deliberately preserves the Grev Home data root. Permanent profile/save/settings deletion requires the explicit `-RemoveUserData` switch.

The uninstall script supports PowerShell `-WhatIf` / `-Confirm` semantics for destructive filesystem operations.

## Data boundary

The executable/install directory and the Grev Home data root are separate boundaries.

The persistent data root remains `C:\GrevHome` unless `GREV_HOME_ROOT` deliberately overrides it. Profiles, GrevIDs, saves, app data, runtime state, packages, presentation overrides, settings, downloads, themes and logs live under that data root.

An application upgrade replaces application binaries only. It must never delete, recreate or overwrite `C:\GrevHome` as part of a normal upgrade.

Uninstall preserves `C:\GrevHome` unless the user explicitly requests `-RemoveUserData`.

## Still outside this contract

Replacing Explorer as the Windows shell is intentionally deferred. Signed release distribution, code signing and an optional graphical installer/updater can be layered on later without changing the application/data boundary or the safe directory-swap upgrade model above.
