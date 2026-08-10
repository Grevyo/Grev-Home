# Grev Home Profiles & Roles

Milestone 0.10 turns local accounts into the reusable Grev Home profile foundation.

## Identity

Each persistent local profile has three identity layers:

- **GrevID** — permanent machine/profile-folder identity.
- **Username** — permanent local username.
- **Display Name** — editable presentation name.

Changing Display Name never renames Username, GrevID or the profile folder.

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

Entering and leaving the photo picker preserves unsaved Display Name, role and avatar choices until Save Profile is selected.

## Still later

Permanent Username migration, profile export/import, profile retirement/deletion workflows, Grev.dad linking and richer online/community profile data remain separate future work.
