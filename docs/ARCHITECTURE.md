# Grev Home architecture

## Non-negotiable rules

1. **Account first** — Grev Home always enters through Login. Local, Guest and future Grev.dad identities all become session users.
2. **Controller first** — a feature is not complete until its normal flow can be completed without a keyboard or mouse.
3. **One shell** — navigation occurs inside one persistent Grev Home window. Features must not create replacement fullscreen application windows.
4. **Runtime stays alive** — launching another application must not terminate Grev Home. The runtime owns controller shortcuts, session tracking and the future overlay/app switcher.
5. **GrevId is persistent; Primary User is not** — each local account receives an immutable GrevId when created. Primary User is only a role inside the current signed-in session and never determines folder names.
6. **Primary user is separate from participants** — a session may contain several signed-in users/controllers while one primary user supplies the GrevId whose profile-specific app data is used for that launch.
7. **Apps are data-driven** — Store, Library, Running Apps and dashboard modules reference common app definitions rather than hard-coded launcher buttons.
8. **Online is additive** — Grev.dad adds sync, levels, community and identity features; it does not replace the local runtime model.
9. **Themes are packages** — presentation is customisable/exportable without allowing themes to own application logic.
10. **Install scope is explicit** — future packages choose GrevId-local or machine-global storage. Shared binaries must not be duplicated just to provide per-user data.
11. **Local identity is portable** — GrevId is safe to use as a folder/package identity so local accounts can later be exported or transferred without depending on a Windows account or machine-specific GUID path.

## GrevId contract

A local account receives a GrevId exactly once at creation.

Format:

```text
GxxxxUsernamexxx
```

- `G` — Grev identity marker.
- `xxxx` — four randomly generated uppercase alphanumeric characters.
- `Username` — a filesystem-safe readable snapshot of the local display name at creation.
- `xxx` — three randomly generated uppercase alphanumeric characters.
- The complete generated ID is collision-checked against known accounts and existing profile directories before it is accepted.
- GrevId matching is case-insensitive because the primary target filesystem is Windows.
- GrevIds contain only ASCII letters, digits and `_`.
- Maximum display-name length is **50 characters**.
- Maximum GrevId path component length is therefore **58 characters**: `G` + 4 + 50 + 3.
- Whitespace/hyphen/underscore runs in the username snapshot become `_`; unsupported characters are omitted. If no safe characters remain, the readable section becomes `User`.
- Renaming the account later changes only `DisplayName`; it never changes GrevId.

Example:

```text
DisplayName: Grev
GrevId:      G4P7KGrev9Q2
```

If the display name later becomes `Grevyo`, the GrevId stays `G4P7KGrev9Q2`.

## Identity model

A persistent local account has a permanent **GrevId** and a changeable display name.

A signed-in runtime user additionally receives a temporary **SessionId**. Controller assignments and the `IsPrimary` flag refer to the runtime SessionId, not to folder names.

```text
Local Account
  GrevId: G4P7KGrev9Q2  ← persistent identity / folder owner
  DisplayName: Grev

Current Session
  SessionId: a1...9e     ← temporary runtime identity
  GrevId: G4P7KGrev9Q2
  IsPrimary: true        ← session role only
```

Changing the Primary User therefore changes which signed-in account's GrevId is used for account-specific launches. It does not rename, move or create profile folders.

## Runtime storage contract

The default data root is `%ProgramData%\Grev Home`. `GREV_HOME_ROOT` may override it for development/testing.

A persistent local GrevId owns:

- `Profiles/<GrevId>/Apps` — binaries/packages intentionally installed only for that local account.
- `Profiles/<GrevId>/AppData` — account-specific configuration/state for shared or local apps.
- `Profiles/<GrevId>/Saves` — account-owned save data where Grev Home manages it.
- `Profiles/<GrevId>/Stats` — local playtime/session/statistics data.
- `Profiles/<GrevId>/Connections` — future external-account connection metadata.
- `Profiles/<GrevId>/Screenshots` — user-owned captures.
- `Profiles/<GrevId>/Themes` — account theme/customisation state.

Machine-level content uses `Global/Apps` and `Global/AppData`. Guest sessions use `_GuestShared` for shared guest data without creating a durable named identity.

This means a future package can express combinations such as **shared binary + per-GrevId data**, **GrevId-local binary + per-GrevId data**, or **fully global app**, without changing the account model.

The portable GrevId also gives future profile export/import a stable identity. An imported local account keeps its existing GrevId so copied saves, settings, playtime and connections stay attached to the same identity. If a destination machine already has that GrevId, import must treat it as the same identity and use an explicit merge/replace/conflict flow rather than silently minting a new ID. The detailed import UI belongs to a later milestone.

## Session and controller model

- Several users may be signed in simultaneously.
- Controllers 1-4 are assignments to session users, not identities themselves.
- A controller can be reassigned without recreating the user session.
- The first signed-in user becomes primary by default; primary can be changed explicitly.
- Primary User is a current-session role only.
- When an app needs account-specific data, Grev Home resolves the Primary User's GrevId and uses `Profiles/<GrevId>/...`.
- Other signed-in session participants remain available for future shared playtime/achievement attribution.
- Disconnecting a controller does not destroy its assignment; reconnecting the same XInput slot can resume it.

## Direct-home runtime contract

The reserved direct-home shortcut is **hold LB + RB + View for 700 ms**. It lives in the input/runtime layer so it can restore Grev Home independently of whichever dashboard view is active. The future overlay and app switcher build on this same runtime boundary.

## Foundation sequence

1. Shell and input foundation — 0.1
2. Persistent portable GrevId accounts, multi-user sign-in, controller assignment and session-only Primary User — 0.2
3. App catalogue and installed library
4. Session/process manager and playtime
5. Overlay and app switcher
6. Package format and Grev Store
7. Controller file manager and system tools
8. Theme engine / Theme Studio
9. Account connections and Grev.dad integration
