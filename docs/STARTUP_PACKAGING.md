# Grev Home startup and packaging contract

Grev Home's first appliance release remains a normal Windows desktop application that starts automatically after Windows sign-in. It does **not** replace Explorer as the Windows shell.

## Runtime ownership

Only one Grev Home process may own the console shell/runtime state in a Windows session. A second launch signals the existing instance to surface itself and then exits.

Controller polling starts only after the MainWindow integration graph is wired, machine/profile state is loaded, and the initial Login route exists.

## Windows startup

A published/installed `GrevHome.exe` registers itself under the current user's Windows Run key. Development executables under build output or temporary folders never register themselves.

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

## Data boundary

The executable/install directory and the Grev Home data root are separate boundaries.

The persistent data root remains `C:\GrevHome` unless `GREV_HOME_ROOT` deliberately overrides it. Profiles, GrevIDs, saves, app data, runtime state, packages, presentation overrides, settings, downloads, themes and logs live under that data root.

An application upgrade must replace application binaries only. It must never delete, recreate or overwrite `C:\GrevHome` as part of a normal upgrade.

Uninstall should preserve `C:\GrevHome` unless the user explicitly chooses a separate remove-user-data action.

## Still outside this contract

Replacing Explorer as the Windows shell is intentionally deferred. Installer technology and update distribution are also separate from this publish contract; whichever installer/updater is chosen later must preserve the data boundary above.
