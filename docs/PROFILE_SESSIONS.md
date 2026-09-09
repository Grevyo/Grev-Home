# Grev Home Profiles, Players & Sessions — Milestone 0.16

Milestone 0.16 finalises Grev Home's local profile, multi-user and controller-session behaviour on top of the 0.15 permanent shell/navigation contract.

## One live session model

`SessionContext` remains the single source of truth for the active Grev Home session.

It owns:

- signed-in users;
- the Primary User;
- local vs guest account kind;
- each signed-in user's current role;
- controller-to-session-user assignments.

Header UI, Dashboard, Login, Profile & Players and app/runtime policy must read this same state rather than maintaining separate player lists.

## Primary User

Exactly one signed-in user is Primary whenever the session is non-empty.

The Primary User determines the active GrevID context for profile-owned apps, per-profile presentation/settings and permission decisions that are explicitly defined as Primary-user actions.

Changing Primary does not sign anyone out and does not discard controller assignments.

If the current Primary User signs out while other users remain, `SessionContext` promotes a remaining signed-in user.

## Controller ownership

A controller assignment belongs to one signed-in session user at a time.

Assigning a controller to another player reassigns that controller; Grev Home does not create duplicate physical-controller ownership records.

A player may remain signed in with no controller assigned.

If an assigned controller disconnects, the session assignment remains visible as disconnected rather than silently signing the player out or losing ownership state. It can be unassigned/reassigned from player management.

Persistent physical-controller identity is still deliberately deferred; assignments are live-session XInput controller indexes only.

## Header profile bubble

The persistent header profile bubble is the quick session surface.

Selecting it opens an in-shell **Who's Playing** flyout rather than navigating away from the current route.

The quick menu shows, for each signed-in player:

- Display Name;
- username/account kind;
- role;
- Primary state;
- assigned controller(s) and disconnected state.

Common actions are available directly where permissions allow:

- View Profile;
- Make Primary;
- Sign Out;
- assign/reassign/unassign connected controllers;
- Add Player;
- Manage Players.

The quick menu uses the same shell flyout interaction layer as Power/System. The route underneath remains unchanged and disabled while the flyout owns controller focus. B/Escape closes the flyout and returns focus to the profile bubble.

## Quick menu vs full management page

The quick menu is for common session actions.

`Profile & Players` remains the larger management route for deeper profile/player/controller work. `Manage Players` from the quick menu opens that route.

The two surfaces must call the same session/profile services; the quick menu must not become a second account implementation.

## Sign-out behavior

Signing out an additional player no longer resets the shell to Dashboard.

If at least one user remains:

- the current Grev Home route stays in place;
- the player/header surfaces refresh;
- a new Primary is selected automatically only when the old Primary left.

Only signing out the final user resets Grev Home to Login.

## Permission boundary

Actions continue to use `AccountAuthorizationService`.

The quick menu does not bypass role policy:

- `ManagePlayers` controls adding/managing other players;
- `ChangePrimaryUser` controls making another signed-in user Primary;
- `AssignControllers` controls controller assignment;
- a user may sign out themselves; managing another user's sign-out requires player-management permission;
- profile viewing/editing continues through existing profile permission rules.

## Controller-first focus contract

The quick menu is bounded and responsive.

- player information wraps rather than clipping;
- the player list scrolls when necessary;
- the card is constrained to available shell height;
- initial focus lands on the first useful player action before footer actions;
- live rebuilds caused by Primary/controller/session changes attempt to restore the same action;
- if that action disappeared (for example `Make Primary` after it succeeds, or `Sign Out` after a player leaves), focus falls back to a valid player action rather than becoming null.

## 0.16 implementation sequence

Pass 1 — implemented / awaiting physical validation:

- profile-bubble quick menu;
- live Primary status;
- controller ownership/reassignment;
- View Profile / Add Player / Manage Players / Sign Out;
- no route reset when an additional player signs out;
- responsive controller-safe flyout focus.

Later 0.16 passes will finalise:

- Login/profile selector presentation and join flow;
- controller disconnect/reconnect policy;
- profile creation/editing polish;
- role/permission UI;
- guest storage/lifecycle architecture;
- GrevID/profile data-boundary audit.

## Physical validation boundary

A green CI build proves compilation only. Profile flyout geometry, controller focus, Primary switching, reassignment, sign-out behavior and Add Player flow are not physically passed until exercised in Grev Home with the controller.
