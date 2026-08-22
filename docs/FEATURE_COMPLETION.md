# Grev Home v1 feature-completion boundary

This document defines the finite feature target that comes **after the backbone** and **before packaging/release work**.

The order is intentionally:

```text
Backbone complete
    ↓
Feature completion
    ↓
Hardening + physical controller/hardware validation
    ↓
Packaging / startup / updater / signed release
```

Packaging is not allowed to pull feature work forward or redefine the shell architecture.

## Backbone now considered present

The following are foundations, not remaining feature ideas:

- one persistent fullscreen `MainWindow` shell;
- controller-first route navigation, Back history and focus restoration;
- local GrevID profiles, roles/permissions, multi-user sessions, Primary User and controller assignment;
- shared controller keyboard and profile/photo flows;
- installed-app catalogue and per-GrevID library membership;
- central runtime sessions, process identity, playtime, Running Apps, App Killer, restart and recovery;
- Grev Overlay and configurable global controller shortcuts;
- machine status and power controls;
- Audio, Display, Wi-Fi and Bluetooth service backbones;
- controller-first Files route;
- Grev Store trusted-package platform;
- Admin Console machine/package boundary;
- per-app controller profiles, launch guides, app settings and presentation overrides;
- Dashboard Continue / Recently Used / real activity totals;
- persistent Activity Center notifications;
- persistent resumable transfer queue with cancellation/retry/restart recovery;
- single-instance shell, sleep/resume handling and crash/session diagnostics.

These systems may still receive bug fixes or UX hardening, but they are no longer reasons to invent another architecture layer.

## Frozen v1 app set

Feature completion uses the four existing applications to finish the platform:

### Profile Apps

- RetroArch
- PCSX2

### Global Apps

- Discord
- Steam

No Dolphin, RPCS3, PPSSPP, DuckStation, Cemu, Xenia or other catalogue expansion occurs before the shell reaches the feature-complete boundary.

## Required feature-completion work

### 1. Put shipped package downloads through the transfer backbone

The transfer queue must become the single observable download path for the four frozen v1 packages and any package prerequisites that Grev Home itself downloads.

Required behavior:

- package downloads appear in Activity Center;
- progress comes from the same transfer state used for recovery;
- cancellation is reflected consistently;
- interrupted resumable downloads can recover after Grev Home restarts;
- completed files still pass each package's existing package-specific verification before install;
- RetroArch/PCSX2 pinned-hash and archive-safety checks remain package-owned;
- no generic arbitrary installer scripting is introduced.

This is the first remaining feature-completion item because the transfer manager must serve real shipped workflows rather than exist only as infrastructure.

### 2. Theme engine + controller-first theme selection

Presentation was deliberately deferred until the behavioral backbone was stable. It can now become a real feature without owning navigation/business logic.

The v1 theme engine must provide:

- built-in Default theme that can always be recovered;
- data-driven theme metadata under `Themes`;
- theme-controlled shell colours/surfaces/accent/muted text and other explicitly theme-owned presentation tokens;
- per-GrevID selected theme;
- controller-first Theme selection inside the existing Settings shell;
- invalid/missing theme data falls back to Default for that session;
- applying/resetting a theme must never alter profile identity, app state, saves, playtime or runtime state.

A richer Theme Studio/editor can build on this engine during feature completion, but the engine and safe selection/fallback contract come first.

### 3. Complete local profile lifecycle

Creation and editing already exist. v1 still needs a deliberate local-account retirement/deletion flow so profiles are not permanent merely because they were created.

Required behavior:

- Admin-gated profile removal;
- final-Admin protection remains enforced;
- signed-in/Primary/controller state is resolved safely before destructive removal;
- explicit two-stage destructive confirmation;
- clear distinction between removing the local identity and preserving/exporting user-owned data where supported;
- never infer or delete another GrevID's directories.

Permanent Username migration and cross-machine import/export are not required for this v1 boundary.

### 4. Finish controller-only edge paths for the existing shell

Every normal v1 flow must be completable without keyboard/mouse.

The feature-completion pass must close proven gaps in:

- dynamic Store/library lists;
- Settings sections;
- Activity Center rows;
- Admin Console operations;
- profile lifecycle dialogs;
- app onboarding/controller-guide flows;
- empty/error/recovery states.

This is implementation work, not a blanket visual redesign.

### 5. Make Activity Center the common actionable status surface

Activity Center already persists notifications and transfer state. Before feature-complete it must be used by meaningful shipped workflows rather than each feature inventing its own durable status store.

Current integration includes:

- download completion/failure;
- trusted package install/update/repair/uninstall result events;
- failed managed runtime sessions;
- runtime restart failures.

Further events should only be added when they are actionable or worth seeing later. Routine focus/navigation events do not belong in notifications.

## Hardware-specific enhancements that do not block the v1 local shell

These remain worthwhile features, but they do not redefine whether the core Grev Home shell is feature-complete:

- creating a brand-new secured Wi-Fi profile/password instead of reconnecting only saved Windows profiles;
- advanced Bluetooth PIN/passkey/manufacturer-specific pairing dialogs;
- multi-monitor topology/placement;
- HDR controls;
- device firmware management.

They can be promoted into the v1 requirement list if physical Grev Machine usage proves one is necessary for ordinary controller-only setup.

## Explicitly deferred beyond local v1

The following must not delay local v1 feature completion:

- Grev.dad online identity/community linking;
- online profile/community features;
- permanent Username migration;
- profile import/export/merge across machines;
- additional external stats providers such as Playnite/RetroAchievements unless separately chosen;
- new emulator/app catalogue expansion;
- unrestricted package scripts;
- packaging/installers/code signing/startup registration/updater/release distribution.

## Feature-complete exit criteria

Grev Home may be called **feature-complete for local v1** when:

1. all four frozen v1 app workflows use the final package/runtime/controller contracts;
2. their Grev-owned downloads are observable through the transfer backbone;
3. the Theme engine and safe controller-first theme selection work;
4. a local profile can be safely created, edited and retired/deleted;
5. normal shell, Store, Admin, Files, Settings, runtime and Activity Center flows have no known keyboard/mouse-only requirement;
6. CI is clean at the chosen feature-complete head;
7. no unresolved architectural workaround is being carried merely to reach packaging.

Only after this boundary do we switch the project into the dedicated hardening/physical-test phase, and only after that do we resume EXE/package/release work.
