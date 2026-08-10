# Grev.dad account connection model

Grev.dad is **not a separate Grev Home login type** and is **not required for Grev Home to work**.

A persistent Grev Home user always starts as a local account with its own immutable GrevID and Username. That local account may later be linked to a Grev.dad account so selected profile/stat/community information can be published online.

The local Grev Home machine remains the source of truth.

## Core model

```text
Local Grev Home account
  GrevID
  Username
  DisplayName
  local playtime
  local achievements
  local profile data
        |
        +-- optional Grev.dad connection
                authentication token
                player/community identity
                upload/sync state
```

Grev.dad does not replace the local account. Linking it must not:

- create a second local profile;
- change the GrevID;
- change the immutable local Username automatically;
- rename or move the profile folder;
- make local sign-in depend on internet access;
- make launching apps depend on internet access;
- make playtime or achievements depend on internet access;
- destroy local data when the connection is removed or unavailable.

## Login behavior

The Grev Home `Who's playing?` screen presents:

- persistent local profiles;
- Guest.

It does not present Grev.dad as another account type.

A local profile that has been linked to Grev.dad may later show a connected badge/status on its existing profile card, but it remains the same local Grev Home account and signs in locally in exactly the same way.

## Grev.dad purpose

Grev.dad is an optional online/community publishing layer.

Examples of information Grev Home may publish for a linked account include:

- GrevID/player identity required to associate the online record;
- chosen public profile information;
- Display Name/profile presentation intended for the online player profile;
- total and per-app/game playtime;
- achievements and achievement progress;
- other locally recorded stats we explicitly choose to expose later.

That online information can then support future Grev.dad features such as:

- a signed-up player directory;
- player profiles;
- friends/following;
- comparing hours, games, stats or achievements;
- activity/community surfaces.

Grev.dad is **not** intended to be the authoritative storage location for the Grev Home profile, saves, settings or runtime state.

## Offline-first contract

Grev Home must behave normally with no internet connection at all.

If Grev.dad is offline, unreachable, slow, under maintenance, DNS fails, authentication expires or the user's connection disappears:

1. local sign-in continues normally;
2. local apps and Grev Home features continue normally;
3. playtime continues to be recorded locally;
4. achievements continue to be recorded locally;
5. profile/stat changes continue to be stored locally;
6. Grev.dad publishing is marked pending/failed without interrupting the user;
7. eligible pending updates can be retried later when connectivity returns.

No normal Grev Home screen should sit waiting for Grev.dad before it can continue.

Connection attempts and publishing should therefore be asynchronous/non-blocking from the user's point of view. Grev Home should use reasonable retry/backoff rather than repeatedly hammering an unavailable server.

## Data direction and conflict rule

For Grev Home-owned stats, the normal direction is:

```text
Local Grev Home state
        ↓
queued/pending online update
        ↓
Grev.dad community/profile copy
```

The Grev.dad copy must not overwrite newer local playtime, achievements or other locally authoritative state simply because the server has older data.

If we later add genuinely server-owned community data, such as a friend request, that data is separate from locally authoritative gameplay/profile state and should have its own explicit model.

## Multiple users

Each persistent local Grev Home account can independently link its own Grev.dad identity. One Grev Home machine may therefore have several GrevIDs, each with a different Grev.dad connection and upload state.

Guest is local/shared temporary usage and does not become a persistent Grev.dad-linked identity.

## Unlinking

Unlinking Grev.dad only removes/disables the online connection and its credentials.

It must not delete the local Grev Home account, GrevID, profile, playtime, achievements, saves, settings or other local data.

The exact API, authentication and queue implementation belongs to the later Account Connections milestone, but this offline-first boundary is non-negotiable.