# Grev Home architecture

## Non-negotiable rules

1. **Account first** — Grev Home always enters through Login. Local, Guest and future Grev.dad identities all become session users.
2. **Controller first** — a feature is not complete until its normal flow can be completed without a keyboard or mouse.
3. **One shell** — navigation occurs inside one persistent Grev Home window. Features must not create replacement fullscreen application windows.
4. **Runtime stays alive** — launching another application must not terminate Grev Home. The runtime owns controller shortcuts, session tracking and the future overlay/app switcher.
5. **Primary user is separate from participants** — future multi-user sessions may contain several signed-in users/controllers while one primary user supplies profile-specific app data.
6. **Apps are data-driven** — Store, Library, Running Apps and dashboard modules reference common app definitions rather than hard-coded launcher buttons.
7. **Online is additive** — Grev.dad adds sync, levels, community and identity features; it does not replace the local runtime model.
8. **Themes are packages** — presentation is customisable/exportable without allowing themes to own application logic.

## Milestone 0.1 boundaries

0.1 proves the shell and input contract only. Local and Guest identities are placeholders and are intentionally not persisted yet. Dashboard feature cards are navigation targets for input testing, not implementations.

The reserved direct-home shortcut is **hold LB + RB + View for 700 ms**. It is implemented in the input/runtime layer so it can still fire while Grev Home itself is not focused. The future overlay will use the same runtime concept but is not part of 0.1.

## Planned foundation sequence

1. Shell and input foundation
2. Persistent profiles and login
3. Multi-user sign-in and controller assignment
4. Primary-user/session participant model
5. App catalogue and installed library
6. Session/process manager and playtime
7. Overlay and app switcher
8. Package format and Grev Store
9. Controller file manager and system tools
10. Theme engine / Theme Studio
11. Account connections and Grev.dad integration
