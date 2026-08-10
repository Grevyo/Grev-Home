# Grev Home runtime sessions

Milestone 0.4 introduces the first real external-app runtime boundary.

## Launch flow

```text
Installed Library
      ↓
select app
      ↓
AppLaunchResolver
      ↓
RuntimeSessionManager
      ↓
Windows process starts
      ↓
Grev Home hides but stays resident
      ↓
process tree + elapsed time tracked
      ↓
process tree exits
      ↓
playtime written to every launch participant
      ↓
Grev Home restores the route it was already on
```

The global **LB + RB + View** hold remains separate from natural app exit. Using that chord while an app is still running explicitly restores the Grev Home Dashboard and leaves the external app/session running.

## Session ownership vs participants

A launch snapshots the current signed-in users.

Example:

```text
Primary User: Grev
Signed in:    Grev, Alfie
```

If Grev launches an emulator that uses `DataStrategy.GrevId`, the app resolves Grev's GrevID for its configuration/data path. The launch session still records both Grev and Alfie as participants.

When the process tree exits, both persistent local accounts receive the same elapsed session time in their own stats files:

```text
Profiles/<Grev-GrevID>/Stats/playtime.json
Profiles/<Alfie-GrevID>/Stats/playtime.json
```

Guest participation is aggregated under `_GuestShared/Stats/playtime.json`.

Changing Primary User later does not change an already-running launch's ownership or participant snapshot.

## Process-tree tracking

Grev Home does not treat only the first PID as the app. The runtime periodically reads the Windows process table and discovers descendants of every already-known PID.

This supports common flows where a starter executable creates another process and exits. A short exit grace period gives child processes time to appear before the Grev Home session is considered ended.

More specialised detached-process matching may use `AppLaunchDefinition.ProcessName` in a later milestone; 0.4 deliberately avoids guessing unrelated same-name processes.

## Safe launch paths

For `SharedBinary` and `GrevIdPortable` installs, `Launch.Executable` must be relative to the app's assigned binary root. Grev Home resolves and verifies that path cannot escape the app directory.

`SystemInstalled` definitions may use a normal Windows executable command or an absolute executable path.

Launch arguments support these tokens:

- `{BinaryRoot}`
- `{DataRoot}`
- `{GrevId}`

The launched process also receives these environment variables where applicable:

- `GREV_HOME_APP_ID`
- `GREV_HOME_BINARY_ROOT`
- `GREV_HOME_GREV_ID`
- `GREV_HOME_APP_DATA`

## Developer smoke test with Notepad

This is a development test only. Grev Home does not seed Notepad or any other fake/demo installed app.

Create this folder under the Grev Home runtime root:

```text
%ProgramData%\Grev Home\Global\Apps\notepad\
```

Create `installed.grevapp.json` inside it:

```json
{
  "Definition": {
    "AppId": "notepad",
    "Name": "Notepad",
    "Kind": "Utility",
    "InstallStrategy": "SystemInstalled",
    "DataStrategy": "NativeAccount",
    "Launch": {
      "Executable": "notepad.exe",
      "Arguments": null,
      "WorkingDirectory": null,
      "ProcessName": "notepad"
    },
    "SupportsController": false,
    "Description": "Development runtime smoke test"
  },
  "Version": "system",
  "InstalledAtUtc": "2026-08-10T10:00:00+00:00",
  "OwnerGrevId": null
}
```

Then:

1. Sign in one or more users.
2. Choose a Primary User.
3. Dashboard → Installed Apps.
4. Select Notepad.
5. Confirm Grev Home hides and Notepad starts.
6. Hold **LB + RB + View** and confirm Grev Home returns to Dashboard while Notepad remains open.
7. Open Running Apps and confirm Notepad is still tracked.
8. Return to Notepad manually and close it.
9. Confirm Running Apps drops back to zero after the exit grace period.
10. Check each persistent participant's `Stats/playtime.json` and confirm the Notepad session was added.

For the natural-return test, launch Notepad again and close it without using the Home chord. Grev Home should restore the Installed Library route that remained active while the shell was hidden.

## 0.4 boundary

0.4 intentionally does not add:

- force close / App Killer actions
- window switching / foreground activation
- global overlay UI
- Store downloads or package installation
- crash/reboot recovery for sessions that were active when Grev Home itself terminated

Those build on this runtime session layer rather than being mixed into it.
