# Grev Home backbone-completion boundary

Grev Home does **not** move into broad feature completion, catalogue expansion, themes or release packaging until the platform backbone is complete enough that adding a new feature no longer requires inventing a new identity, storage, network, permissions, runtime or shell contract.

The project order is:

```text
Backbone completion
    ↓
Feature completion / final feature additions
    ↓
Visual/theme/catalogue expansion
    ↓
Hardening + physical controller/hardware validation
    ↓
Packaging / startup / updater / signed release
```

Apps and themes are not evidence that the backbone is complete. They consume the backbone after it exists.

## Non-negotiable local-first rule

**Grev.dad is always optional.**

A Grev Home machine and every permanent local GrevID must remain fully usable without ever creating, linking or contacting a Grev.dad account. Grev.dad is an additive online/social/cloud service only.

A Grev.dad connection must never become:

- a Grev Home login requirement;
- an entitlement check for local features;
- an app-launch or runtime requirement;
- a save/configuration dependency;
- a requirement for local playtime, history, XP, level or milestones;
- a requirement for machine Admin/Standard/Guest permissions;
- a requirement for controller, Files, Settings or other local appliance functions.

If Grev.dad is unavailable, revoked, expired or never linked, Grev Home continues locally. Online-only/social data may be stale or unavailable, but the appliance itself does not degrade.

## Backbone already present

The following foundations already exist and should be extended rather than replaced:

- one persistent fullscreen `MainWindow` shell;
- controller-first route navigation, Back history and focus restoration;
- permanent local GrevID + Username identity, editable DisplayName;
- local profiles, Admin/Standard/Guest roles, permissions and Primary User;
- multi-user local sessions and controller assignment;
- local profile storage rooted by GrevID;
- central app catalogue/install registration contracts;
- central runtime/process/playtime contracts;
- controller shortcuts and Grev Overlay;
- machine settings/service boundaries;
- Files, Store and Admin route boundaries;
- Dashboard activity data;
- Activity Center notification storage;
- persisted transfer infrastructure;
- single-instance shell, sleep/resume and crash/session diagnostics.

These remain backbone and may still need hardening, but they are no longer separate architectures to reinvent.

## Backbone still required before feature completion

### 1. Optional Grev.dad online identity link

A local Grev Home profile may link to exactly one Grev.dad account without replacing its local GrevID or requiring the website password to be stored on the PC. Choosing not to link must leave all local Grev Home functionality intact.

Required contracts:

- local GrevID remains the stable on-machine/profile-tree identity;
- Grev.dad keeps its own server-side user UUID as its database identity;
- the relationship between them is explicit, optional and revocable;
- linking uses a one-time approval/device flow rather than copying browser cookies;
- Grev Home receives a dedicated revocable device credential after the user approves the link on Grev.dad;
- the server stores only hashes of device credentials;
- the local secret is stored with a Windows-protected secret mechanism, not plain JSON;
- unlink/revoke works from either side;
- account suspension/disable/revocation immediately prevents online API use without breaking the local Grev Home account;
- no network outage may stop a linked local profile from signing into or using Grev Home locally.

### 2. Online account/session service in Grev Home

The shell needs one network/account service boundary that owns Grev.dad communication.

It must provide:

- linked/unlinked/offline/expired/revoked/error state;
- remote account summary (`userId`, username, display name, verification state);
- credential refresh/revalidation;
- explicit disconnect/unlink;
- cancellation/timeouts and offline-safe caching;
- no feature-specific direct HTTP calls scattered across views;
- events/snapshots that future Friends, Activity, profile-sync and community features consume.

### 3. Grev.dad API boundary for Grev Home

Grev.dad has browser-cookie authentication and existing profile/community/presence systems. Grev Home uses a separate API authentication boundary for optional linked desktop clients.

The desktop API initially exposes only the minimum identity/session surface, then becomes the common authenticated path used by later Grev Home social features.

Minimum backbone endpoints:

- begin link request;
- approve/deny link while signed into Grev.dad in a browser;
- poll/complete link request using the one-time device secret;
- get linked account identity;
- revoke current device credential;
- revoke a GrevID link/account-device relationship.

### 4. Social graph contract

Grev.dad already has member discovery, profile subscriptions, profile interaction and presence concepts, but Grev Home needs a stable social-graph contract before a Friends UI is added.

The backbone must decide and persist:

- friend request states;
- accepted friendship ownership;
- block interaction with friendship;
- who may discover/request whom;
- server-side privacy/permission filtering;
- friend removal and request cancellation;
- API payload shape reused by the site and Grev Home.

A Friends screen is a feature. The friendship model/API it relies on is backbone.

### 5. Presence + activity contract

Grev.dad already has `user_presence`, notifications and profile/content activity concepts. Grev Home connects to those through an explicit device-authenticated contract rather than inventing a second incompatible activity system.

Backbone requirements:

- publish local shell presence (`online`, `away`, `busy`, `offline`) when linked;
- publish high-level activity such as `playing <app>` where allowed;
- expiry/heartbeat semantics so a crashed/offline PC does not remain online forever;
- privacy-controlled visibility;
- friend presence/activity read API;
- activity event schema for durable events that belong in a feed;
- clear separation between ephemeral presence and durable activity/history.

The visual Friends/Activity feed comes later.

### 6. Local session history + progression bridge

Grev Home owns its own progression whether or not Grev.dad exists.

Backbone requirements:

- every completed managed-app run is written to a durable GrevID-owned local session-history journal;
- session history records have stable unique IDs so optional online replay is idempotent;
- aggregate playtime remains local and independent from online sync;
- Grev Home calculates its own XP, level and milestones locally from its own trusted local data;
- Grev.dad never sends a level/XP value that Grev Home is required to accept;
- when a GrevID is linked, Grev Home may upload completed-session history to Grev.dad;
- sessions completed while offline remain local and can be uploaded later;
- replaying/retrying the same session must not create duplicate history;
- Grev.dad stores the Grev Home XP contribution separately enough to make its source clear and only credits increases in the Grev Home high-water mark;
- the linked Grev Home XP contribution may increase the broader Grev.dad account XP/level alongside XP earned directly on Grev.dad;
- unlinking does not remove, reduce or invalidate the local Grev Home level/history;
- remote deletion/revocation can remove online copies/credentials without touching local Grev Home history.

This allows Grev.dad later to show game/app history, recent activity, playtime and Grev Home-derived progression while remaining purely optional.

### 7. Online/offline data ownership rules

Before social/profile-sync features are added, every field needs a declared authority:

- local-only;
- server-only;
- mirrored with local authority;
- mirrored with server authority;
- mergeable/event-based.

At minimum this must cover Username, DisplayName, avatar, bio/status, GrevID, website user UUID, roles/permissions, friends, presence, activity, local session history and progression.

Local Grev Home Admin/Standard/Guest roles must **not** silently become Grev.dad site roles, and Grev.dad verification/admin state must **not** silently grant machine permissions.

### 8. Network resilience and sync queue

Online integration must not make the appliance dependent on grev.dad availability.

Required behaviour:

- bounded request timeouts;
- retry only safe/idempotent operations;
- cached last-known remote identity/social snapshots;
- completed history/progression uploads can queue/replay safely after connectivity returns;
- queued presence expires and stale presence is never replayed;
- clear offline state instead of blocking shell navigation;
- malformed or incompatible server data cannot crash the shell;
- API schema/version compatibility is explicit.

## Backbone completion exit criteria

Backbone is complete only when all of the following are true:

1. Grev Home is fully usable with no Grev.dad account, no network and no online configuration;
2. a user who chooses to can securely link/unlink a local GrevID to a Grev.dad account;
3. the optional link survives Grev Home restarts without storing a plaintext site password/token;
4. Grev Home can authenticate to a dedicated grev.dad desktop API and retrieve its linked remote identity;
5. local sign-in and every local appliance foundation still work normally with no internet or when grev.dad is unavailable;
6. friendship, presence and activity have server-side models/API contracts even if their final UI is not built yet;
7. completed local session history exists independently of Grev.dad and can be replayed idempotently when a link is available;
8. Grev Home XP/level remains local-authoritative while an optional linked contribution can affect Grev.dad progression without creating a reverse dependency;
9. future Friends, Activity, game-history, profile-sync and community screens can be implemented as consumers of these contracts rather than creating new identity/network systems;
10. CI is clean and the shell has no known architectural blocker requiring another foundation rewrite.

Only then do we deliberately ask: **what final features do we still want before release?**

## Deliberately after backbone

These are features/presentation work and must not distract from the backbone gate:

- additional emulator/app catalogue work;
- moving every installer download through the transfer UI;
- Theme engine / Theme Maker / visual theme packs;
- Friends list screen and social UI polish;
- Activity feed UI;
- visual game-history/profile presentation;
- expanded profile/community presentation;
- final profile deletion/retirement UX;
- broad visual polish;
- packaging, EXE distribution, startup registration, updater and signing.
