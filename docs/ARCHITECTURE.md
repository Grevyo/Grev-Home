# Grev Home architecture

## Non-negotiable rules

1. **Account first** — Grev Home always enters through Login. Local, Guest and future Grev.dad identities all become session users.
2. **Controller first** — a feature is not complete until its normal flow can be completed without a keyboard or mouse.
3. **One shell** — navigation occurs inside one persistent Grev Home window. Features must not create replacement fullscreen application windows.
4. **Runtime stays alive** — launching another application must not terminate Grev Home. The runtime owns controller shortcuts, session tracking and the future overlay/app switcher.
5. **Primary user is separate from participants** — a session may contain several signed-in users/controllers while one primary user supplies profile-specific app data.
6. **Apps are data-driven** — Store, Library, Running Apps and dashboard modules reference common app definitions rather than hard-coded launcher buttons.
7. **Online is additive** — Grev.dad adds sync, levels, community and identity features; it does not replace the local runtime model.
8. **Themes are packages** — presentation is customisable/exportable without allowing themes to own application logic.
9. **Profile identity is immutable** — local profile directories use permanent GUIDs, never display names, so renaming a user cannot break app or save paths.
10. **Install scope is explicit** — future packages choose profile-local or machine-global storage. Shared binaries must not be duplicated just to provide per-user data.

## Runtime storage contract

The default data root is `%ProgramData%\Grev Home`. `GREV_HOME_ROOT` may override it for development/testing.

A persistent local profile owns:

- `Profiles/<profile-guid>/Apps` — binaries/packages intentionally installed only for that Grev Home profile.
- `Profiles/<profile-guid>/AppData` — profile-specific configuration/state for shared or local apps.
- `Profiles/<profile-guid>/Saves` — profile-owned save data where Grev Home manages it.
- `Profiles/<profile-guid>/Stats` — local playtime/session/statistics data.
- `Profiles/<profile-guid>/Connections` — future external-account connection metadata.
- `Profiles/<profile-guid>/Screenshots` — user-owned captures.
- `Profiles/<profile-guid>/Themes` — profile theme/customisation state.

Machine-level content uses `Global/Apps` and `Global/AppData`. Guest sessions use `_GuestShared` for shared guest data without creating a durable named identity.

This means a future package can express combinations such as **shared binary + per-profile data**, **profile-local binary + per-profile data**, or **fully global app**, without changing the profile model.

## Session and controller model

- Several users may be signed in simultaneously.
- Controllers 1-4 are assignments to session users, not identities themselves.
- A controller can be reassigned without recreating the user session.
- The first signed-in user becomes primary by default; primary can be changed explicitly.
- The Primary User will own the app/config/save context used for launches.
- Other signed-in session participants remain available for future shared playtime/achievement attribution.
- Disconnecting a controller does not destroy its assignment; reconnecting the same XInput slot can resume it.

## Direct-home runtime contract

The reserved direct-home shortcut is **hold LB + RB + View for 700 ms**. It lives in the input/runtime layer so it can restore Grev Home independently of whichever dashboard view is active. The future overlay and app switcher build on this same runtime boundary.

## Foundation sequence

1. Shell and input foundation — 0.1
2. Persistent profiles, multi-user sign-in, controller assignment and primary user — 0.2
3. App catalogue and installed library
4. Session/process manager and playtime
5. Overlay and app switcher
6. Package format and Grev Store
7. Controller file manager and system tools
8. Theme engine / Theme Studio
9. Account connections and Grev.dad integration
