# Grev Home Profiles & Roles

Milestone 0.10 turns local accounts into the reusable Grev Home profile foundation.

## Identity

Each persistent local profile has three identity layers:

- **GrevID** — permanent machine/profile-folder identity.
- **Username** — permanent local username.
- **Display Name** — editable presentation name.

Changing Display Name never renames Username, GrevID or the profile folder.

Profiles also have:

- an optional local **Status / Tagline** of up to 60 characters;
- an optional local **About** field of up to 160 characters.

Both are presentation data and are edited through the controller-first popup keyboard.

## Roles and permissions

Roles are persistent profile data. Authorization is centralized through `AccountAuthorizationService` rather than scattered role checks.

Initial roles:

- **Admin** — full current Grev Home administration.
- **Standard** — normal player/app/files/session/controller use without account or machine administration.
- **Guest** — restricted player use with app launch, own-profile editing and assigned-controller use.

The first profile is always Admin and Grev Home refuses to demote the final remaining Admin.

## Profile pictures

Profiles can use either a built-in avatar preset or a custom local photo.

Built-in avatar tiles are intentionally compact symbol-only choices. Their name is shown outside the tile so focus/highlight changes do not cause text overflow.

Custom photos are selected through Grev Home's own controller-first photo picker. Supported input formats are PNG, JPG, JPEG and BMP, up to 10 MB.

When a custom photo is saved, Grev Home copies the image into that GrevID's profile root and stores only the local avatar filename in `profile.json`. The original source image may then be moved or deleted without breaking the profile picture.

Custom photos are rendered on Login, Profile & Players, View Profile and Edit Profile.

## Text entry

Create Account and Edit Profile use the shared `ControllerQwertyKeyboard` overlay rather than permanent inline keyboards.

The keyboard is an internal Grev Home surface inside the existing `MainWindow` and contains:

- number row 1–0;
- QWERTY row;
- ASDF row;
- ZXCV row;
- Shift for upper/lower case;
- Space;
- Backspace;
- Clear;
- Done;
- Cancel.

Controller directional focus is kept inside the keyboard while it is open. B cancels the keyboard rather than leaving the underlying account/profile page.

## Grev Level and activity

View Profile includes a local progression/activity layer driven by real Grev Home runtime data rather than a manually editable level number.

The first profile stats source is **Grev Home**. It reads each GrevID's existing `Stats\playtime.json` and combines it with currently active managed runtime sessions for that GrevID.

The profile displays:

- Grev Level;
- XP progress toward the next level;
- total Grev Home tracked time;
- completed session count;
- unique managed apps played;
- currently running managed app count;
- last tracked activity;
- Recent Activity, ordered by the last activity time for each managed app;
- Top Played apps;
- earned/locked Milestones with progress;
- Connected Stats sources.

### Initial XP contract

Grev XP is deterministic and reconstructable from Grev Home data:

- **1 XP per tracked minute**;
- **20 XP per completed managed-app session**;
- **100 XP per unique managed app played**.

The XP required to advance from the current level is:

```text
250 + ((current level - 1) × 150)
```

Level starts at 1. Progression is calculated from activity and is not stored as an authoritative editable number.

Live managed-app time can contribute to the displayed total/XP while an app is running. Session-completion XP is only added once the managed session has actually completed.

## Milestones

Milestones are calculated from the same authoritative Grev Home activity rather than stored as independent unlock flags. This means they can be rebuilt safely from profile stats.

The initial baseline includes milestones for:

- first completed managed-app session;
- 1, 10 and 100 Grev Home tracked hours;
- 5 and 20 unique managed apps;
- 50 completed sessions;
- reaching Grev Level 5 and Level 10.

Locked milestones show their current progress. Earned milestones remain earned because the underlying activity totals remain authoritative.

## Connected Stats provider model

Profile activity is provider-based through `IProfileStatsSource`.

`GrevHomeProfileStatsSource` is the first real provider. Future providers can independently contribute profile-facing data without changing the permanent identity model. Intended examples include:

- Playnite library/playtime/history;
- emulator-specific playtime and game history;
- RetroAchievements achievements/progress;
- Steam/Xbox/other account data where a supported connection exists;
- game-specific statistics providers.

External/imported providers do **not** automatically increase Grev Level. This prevents the same hours from being double-counted when, for example, Grev Home and Playnite both know about the same gaming session. A future progression policy can explicitly decide which external achievements or events should contribute XP.

The Connected Stats area only shows providers that actually exist. Grev Home does not fabricate a Playnite or emulator connection before one has been built/configured.

## Signed-in players and controllers

A signed-in player and a controller assignment are separate session concepts.

A controller can be:

- assigned to a signed-in player;
- reassigned to another signed-in player;
- **unassigned without signing that player out**.

After unassignment the player remains in the current session with `No controller assigned` and can receive a controller again later.

Each signed-in player can also expose View, Edit, Sign Out and Make Primary actions where the current Primary User's role permits them.

## Custom photo picker

The profile photo picker stays inside Grev Home rather than opening a separate fullscreen WPF window. It can browse normal user locations and ready drives, shows folders plus supported image files, and returns the chosen image to the existing profile-edit draft.

Entering and leaving the photo picker preserves unsaved Display Name, Status / Tagline, About, role and avatar choices until Save Profile is selected.

## Boundaries before app work

Pinned/favourite games, favourite platform, game artwork showcases and similar app-backed profile content should be added once the Grev Home app library/installer supplies real selectable app identities. The profile should not store fake placeholder games just to make the page look fuller.

Profile content and progression are data. Themes are presentation. Future theme changes must not alter GrevID, profile metadata, progression, milestones, playtime or connected provider data.

## Still later

Permanent Username migration, profile export/import, profile retirement/deletion workflows, Grev.dad linking and richer online/community profile data remain separate future work.

The next stats-provider work can add a real Playnite adapter and/or emulator/RetroAchievements providers without redesigning View Profile.
