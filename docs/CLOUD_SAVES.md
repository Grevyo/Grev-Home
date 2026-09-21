# Cloud saves

An optional bridge from a local GrevID's per-app save data (`Profiles/<GrevID>/Saves/<AppId>`) to
Grev.dad, so a save can be restored on another Grev Home machine - the same non-negotiable
local-first rule as every other Grev.dad feature in `docs/GREV_DAD.md`: the local save folder
stays authoritative, cloud saves are opt-in per app, and Grev.dad being unreachable never blocks
or interrupts launching or playing that app.

## Scope

Cloud saves cover whatever a package already writes into its GrevID-owned save folder - for
example, RetroArch and PCSX2 already redirect SaveRAM/save states there (see `docs/RETROARCH.md`,
`docs/PCSX2.md`). This is transport only: it does not know anything about save file formats, does
not merge saves, and does not attempt automatic conflict resolution beyond reporting that local
content has changed since the last sync.

## `GrevDadSaveSyncService`

Follows the same contract `GrevDadProfileSyncService` already establishes for progression:
`GrevDadAccountService` remains authoritative for link validity, and this service only transports
local data after that authority confirms the device link.

- `IsEnabledAsync` / `SetEnabledAsync` - per-app, per-GrevID opt-in, off by default. Enabling cloud
  saves for one app never enables it for another.
- `GetStatusAsync` - never makes a network call. Computed from local state (the enabled flag, a
  local manifest, and a content hash of the save folder) plus the account service's already-cached
  link snapshot, so checking status never slows down or blocks opening Settings.
- `UploadAsync` / `DownloadAsync` - the only calls that touch the network. Both return a fully
  formed `CloudSaveState` rather than throwing for expected outcomes (disabled, not linked,
  offline, nothing to sync yet); only a genuinely unexpected failure surfaces as
  `CloudSaveStatus.Error`.
- `CheckRemoteAsync` - a lightweight network call (`HEAD`, no archive download) that tells a real
  conflict apart from an ordinary one-sided change: it compares the local content hash against the
  hash as of the last upload/download, and the remote's `X-Grev-Updated-At` timestamp against the
  remote timestamp Grev Home last knew about, and reports `CloudSaveStatus.Conflict` only when
  *both* sides changed since that last sync. Only local changed keeps reporting
  `LocalChangesPending`; only remote changed reports `RemoteChangesAvailable`. Best-effort by
  design - every caller falls back to the network-free `GetStatusAsync` on failure rather than
  blocking on it.
- `ResolveConflictAsync(grevId, appId, keepLocal)` - the explicit choice a conflict needs: `true`
  pushes this device's save over the remote one (an ordinary upload), `false` pulls the remote save
  down over this device's (an ordinary download, with the same non-destructive backup-before-replace
  safety). There is no merge - cloud saves are transport only and never inspect save content.
- `GetEnabledAppsAsync` - every app under a GrevID that currently has cloud saves turned on, read
  from the per-app manifest files since there is no separate index. Used by the sign-in sweep below.

### Safety

- **Downloads never overwrite in place.** The archive is fully extracted to a staging directory
  first; only once that succeeds is the existing local save folder moved aside to a timestamped
  `<AppId>.backup-<timestamp>` sibling and the staged save moved into its place. A truncated
  download or a failure partway through extraction can never leave a half-written save folder, and
  a bad restore can always be recovered from its backup.
- The staging directory is a sibling of the save folder itself (under the same `Saves` root), not
  the system temp directory - `Directory.Move` fails across drives on Windows, and Grev Home's root
  is not guaranteed to share a drive with `%TEMP%`.
- Archives over 300 MB are rejected before upload rather than silently accepted, as a guard against
  a save folder pointed at something that isn't really a save.
- The local content hash (`GrevDadSaveSyncService.ComputeLocalHash`, internal but exposed for
  testing) is order-independent over every file's relative path and bytes; it is used only to
  answer "has anything changed since the last sync" locally and is never itself sent anywhere.

## Automatic upload, manual restore

`GrevDadCoordinator.QueueSaveSyncAfterLocalHistory` is subscribed to the same
`RuntimeSessionManager.SessionHistoryCommitted` event `QueueSyncAfterLocalHistory` already uses for
progression sync (`MainWindow.SessionHistory.cs`): after a completed session's local history
commits, if that app has cloud saves enabled for its primary participant, Grev Home first runs the
same lightweight `CheckRemoteAsync` conflict check Settings uses. If it comes back clean the current
save data uploads in the background, same as before; if it detects a genuine conflict (the save also
changed on another device since the last sync), the upload is skipped and a Warning notification is
published instead - a conflict is never silently resolved by whichever device happens to close the
app last. A failure here does not retry on a timer the way progression sync does - the next
completed session, or a manual Sync Now, tries again.

Downloading is always manual (Settings > this app > Cloud Saves > Restore from Cloud, or the
conflict panel's Keep/Use Cloud choice). Grev Home does not download and silently apply a cloud save
before launch: the player decides when a restore happens, and Restore from Cloud is disabled until
there is something to restore.

## Sign-in sweep

`GrevDadCoordinator` also runs a one-time check the first time each GrevID appears in
`SessionContext.SignedInUsers` during a Grev Home run (mirroring the sign-in edge
`GrevDadProfileSyncService` already reacts to via `_session.Changed`): for every app
`GetEnabledAppsAsync` reports as opted in, it calls `CheckRemoteAsync` and publishes one summary
notification through the existing Activity Center `NotificationService` - a Warning if any app has a
genuine conflict, otherwise an Info if any app has a newer cloud save waiting. Nothing downloads or
uploads automatically from this sweep; it only surfaces what needs the player's attention in
Settings. Like every other Grev.dad call, a failure or timeout here is swallowed rather than
delaying or blocking sign-in.

## Conflict resolution

A conflict only exists when *both* sides changed since Grev Home's last known-common state for that
app - an ordinary one-sided change (only local, or only remote) is not a conflict and is handled by
the existing Sync Now / Restore from Cloud actions. When App Settings > Cloud Saves shows a conflict,
it presents two explicit choices instead of a status line: **Keep This Device's Save** (upload,
overwriting the remote copy) or **Use Cloud Save** (download, overwriting the local copy with the
same backup-before-replace safety every restore already has). Neither Grev Home nor Grev.dad ever
merges the two - cloud saves are transport only and have no save-format awareness to merge with.

## Settings surface

App Settings > Cloud Saves (`AppSettingsView`, mirroring the page's existing General/Presentation/
Controller Profile sections): a toggle, a status line built from `CloudSaveState`, and Sync Now /
Restore from Cloud buttons. The view only displays whatever state it is given and raises intent
events; `MainWindow.AppSettings.cs` owns every actual `GrevDadSaveSyncService` call, the same
separation the page's other sections already keep.

## Storage

- Local manifest (enabled flag, last-uploaded content hash, last upload/download timestamps): one
  JSON file per app under `Profiles/<GrevID>/Connections/GrevDad/cloud-saves/<AppId>.json`,
  matching where `GrevDadProfileSyncService` already keeps its own sync cursor
  (`Connections/GrevDad/sync.json`).
- Remote: `PUT`/`GET api/grev-home/saves/{appId}`, bearer-authenticated with the same per-GrevID
  device credential every other Grev.dad call uses.
