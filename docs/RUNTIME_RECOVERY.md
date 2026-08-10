# Milestone 0.9 — Runtime Recovery & Overlay Hardening

Milestone 0.9 makes Grev Home's managed external-app runtime survive Grev Home itself being closed or restarted while an app remains open.

## Runtime state location

Grev Home persists active managed sessions to:

```text
C:\GrevHome\Data\Runtime\sessions.json
```

The file is written atomically through `sessions.json.tmp` and replaced only after serialization completes.

Runtime persistence is recovery metadata, not an app catalogue and not a source of truth for installed applications.

## Persisted session identity

Each active session records:

- LaunchSessionId
- AppID and app display name
- Primary GrevID captured at launch
- launch participants captured at launch
- launch time
- last time Grev Home observed an exact tracked process alive
- runtime state
- original root PID
- every tracked process as **PID + Windows process start time**

PID alone is never sufficient for recovery.

Windows can reuse a PID after a process exits. A recovered process must match both the persisted PID and its persisted process start time before Grev Home treats it as the same process.

The same exact-identity validation is used before Switch, graceful Close and Force Close.

## Recovery behaviour

When Grev Home starts, RuntimeSessionManager reads the persisted state before the normal shell finishes loading.

For each persisted session:

1. validate the recovery record;
2. validate each saved process identity against Windows;
3. recover the session only if at least one exact process is still alive;
4. restore the original AppID, Primary GrevID, participants, launch time and tracked process identities;
5. restart the normal process-tree monitor;
6. expose that session through the existing Running Apps and Grev Overlay runtime surfaces.

A recovered session therefore remains the same LaunchSessionId rather than becoming a fake new launch.

## Stale sessions and playtime safety

If no exact saved process is alive when Grev Home returns, the recovery record is discarded.

Grev Home deliberately does **not** guess when that app ended and does not add uncertain playtime from a stale recovery record. This avoids duplicate or fictional playtime if Grev Home previously crashed during finalization.

If the app is still alive when Grev Home returns, monitoring resumes and the normal eventual app exit records the full session duration from the original launch time.

## Heartbeat

While a managed app remains alive, Grev Home updates the persisted runtime state periodically and whenever important state changes, including:

- a new child process is discovered;
- the app enters Closing state;
- a session starts;
- a session ends;
- Grev Home itself is closing.

A runtime-state write failure never intentionally terminates the managed app or shell. Runtime continues in memory.

## Restart App

Restart App is available from Running Apps and from the Grev Overlay for the current managed app.

Restart is a managed runtime operation:

1. snapshot the original session's AppID, Primary GrevID and launch participants;
2. verify the same AppID is still registered for the original launch profile;
3. request a normal graceful close;
4. wait for the exact tracked processes to exit;
5. if necessary, escalate to the existing managed force-close path;
6. refuse to launch a duplicate if the old exact process remains alive;
7. relaunch the registered AppID using the original Primary GrevID and participants;
8. create a new LaunchSessionId for the replacement process tree.

Restart never trusts a persisted executable command or arbitrary path from the recovery JSON. The AppID must still resolve through Grev Home's installed-app model.

## Boundaries

0.9 does not yet persist the Grev Home signed-in lobby itself. After Grev Home restarts, a still-running external app can be recovered independently of whether users have signed back into the shell.

0.9 also does not invent an end time for an app that both started and ended while Grev Home was absent.

These are deliberate safety boundaries rather than silent guesses.

## Physical acceptance test

With one real test application registered through Grev Home's current development app-registration path:

1. sign in and launch the app from Installed Apps;
2. verify Grev Home hides while the app runs;
3. use Return Home and confirm the app remains listed in Running Apps;
4. switch back to the app;
5. open Grev Overlay and verify Resume, Switch App, Restart, Close, Running Apps, App Killer and Return Home are reachable by controller;
6. choose Restart and verify the old process closes before the replacement starts;
7. launch the app again, leave it running, close Grev Home itself, then start Grev Home again;
8. verify the existing external app is recovered as a Running App rather than duplicated;
9. verify Switch / Overlay actions still target that recovered process;
10. close the recovered app and verify the runtime session clears normally.

A PID shown in diagnostics may change after Restart. The recovered pre-restart session must never attach to an unrelated process that merely reused an old PID.
