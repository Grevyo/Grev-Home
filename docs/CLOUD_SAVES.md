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
commits, if that app has cloud saves enabled for its primary participant, the current save data
uploads in the background. A failure here does not retry on a timer the way progression sync
does - the next completed session, or a manual Sync Now, tries again.

Downloading is always manual (Settings > this app > Cloud Saves > Restore from Cloud). Grev Home
does not download and silently apply a cloud save before launch: the player decides when a restore
happens, and Restore from Cloud is disabled until there is something to restore.

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
