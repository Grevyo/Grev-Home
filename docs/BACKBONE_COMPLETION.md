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

### 1. Grev.dad online identity link

A local Grev Home profile must be able to link to exactly one Grev.dad account without replacing its local GrevID or requiring the website password to be stored on the PC.

Required contracts:

- local GrevID remains the stable on-machine/profile-tree identity;
- Grev.dad keeps its own server-side user UUID as its database identity;
- the relationship between them is explicit and revocable;
- linking uses a one-time approval/device flow rather than copying browser cookies;
- Grev Home receives a dedicated revocable device credential after the user approves the link on Grev.dad;
- the server stores only hashes of device credentials;
- the local secret is stored with a Windows-protected secret mechanism, not plain JSON;
- unlink/revoke works from either side;
- account suspension/disable/revocation immediately prevents online API use without breaking the local Grev Home account;
- no network outage may stop a linked local profile from signing into Grev Home locally.

### 2. Online account/session service in Grev Home

The shell needs one network/account service that owns all Grev.dad communication.

It must provide:

- linked/unlinked/offline/expired/revoked/error state;
- remote account summary (`userId`, username, display name, verification state);
- credential refresh/revalidation;
- explicit disconnect/unlink;
- cancellation/timeouts and offline-safe caching;
- no feature-specific direct HTTP calls scattered across views;
- events/snapshots that future Friends, Activity, profile-sync and community features consume.

### 3. Grev.dad API boundary for Grev Home

Grev.dad currently has browser-cookie authentication and existing profile/community/presence systems. Grev Home needs a separate API authentication boundary for trusted linked desktop clients.

The desktop API must initially expose only the minimum identity/session surface, then become the common authenticated path used by later Grev Home social features.

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

Grev.dad already has `user_presence`, notifications and profile/content activity concepts. Grev Home must connect to those through an explicit device-authenticated contract rather than inventing a second incompatible activity system.

Backbone requirements:

- publish local shell presence (`online`, `away`, `busy`, `offline`);
- publish high-level activity such as `playing <app>` where allowed;
- expiry/heartbeat semantics so a crashed/offline PC does not remain online forever;
- privacy-controlled visibility;
- friend presence/activity read API;
- activity event schema for durable events that belong in a feed;
- clear separation between ephemeral presence and durable activity/history.

The visual Friends/Activity feed comes later.

### 6. Online/offline data ownership rules

Before social/profile-sync features are added, every field needs a declared authority:

- local-only;
- server-only;
- mirrored with local authority;
- mirrored with server authority;
- mergeable/event-based.

At minimum this must cover Username, DisplayName, avatar, bio/status, GrevID, website user UUID, roles/permissions, friends, presence and activity.

Local Grev Home Admin/Standard/Guest roles must **not** silently become Grev.dad site roles, and Grev.dad verification/admin state must **not** silently grant machine permissions.

### 7. Network resilience and sync queue

Online integration must not make the appliance dependent on grev.dad availability.

Required behaviour:

- bounded request timeouts;
- retry only safe/idempotent operations;
- cached last-known remote identity/social snapshots;
- queued outbound presence/activity events where useful, with expiry so stale presence is never replayed;
- clear offline state instead of blocking shell navigation;
- malformed or incompatible server data cannot crash the shell;
- API schema/version compatibility is explicit.

## Backbone completion exit criteria

Backbone is complete only when all of the following are true:

1. a local GrevID profile can securely link/unlink to a Grev.dad account;
2. the link survives Grev Home restarts without storing a plaintext site password/token;
3. Grev Home can authenticate to a dedicated grev.dad desktop API and retrieve its linked remote identity;
4. local sign-in still works normally with no internet or when grev.dad is unavailable;
5. friendship, presence and activity have server-side models/API contracts even if their final UI is not built yet;
6. future Friends, Activity, profile-sync and community screens can be implemented as consumers of those contracts rather than creating new identity/network systems;
7. CI is clean and the shell has no known architectural blocker requiring another foundation rewrite.

Only then do we deliberately ask: **what final features do we still want before release?**

## Deliberately after backbone

These are features/presentation work and must not distract from the backbone gate:

- additional emulator/app catalogue work;
- moving every installer download through the transfer UI;
- Theme engine / Theme Maker / visual theme packs;
- Friends list screen and social UI polish;
- Activity feed UI;
- expanded profile/community presentation;
- final profile deletion/retirement UX;
- broad visual polish;
- packaging, EXE distribution, startup registration, updater and signing.
