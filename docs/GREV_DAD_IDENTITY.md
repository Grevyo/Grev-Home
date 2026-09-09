# Grev Home ↔ Grev.dad identity backbone

Grev Home and Grev.dad have related identities, but they are **not the same identity store**.

A local Grev Home account must continue working as a complete local account if Grev.dad is offline, the internet is unavailable, the remote account is suspended, or the user deliberately unlinks the website.

## Permanent identities

### Grev Home

The permanent local identity is the GrevID.

- GrevID owns the local profile directory and local machine data.
- Username is part of the local identity contract and is not silently rewritten from the website.
- Display Name is local presentation and may be edited locally.
- Admin / Standard / Guest are **machine permissions only**.

### Grev.dad

The permanent server identity is the existing Grev.dad user UUID.

- Grev.dad username belongs to the website account.
- Grev.dad display name belongs to the website account.
- site verification / Owner / Administrator state belongs to Grev.dad only.

The old Grev.dad `users.grev_id` model was removed from the site and must not be reintroduced as the server primary key.

## Link relationship

A link is an explicit one-to-one relationship:

```text
local GrevID  ⇄  grev.dad user UUID
```

The relationship may have multiple device credentials over time so the same GrevID can later be used on another authorised Grev Home machine without changing either permanent identity.

Linking must never:

- replace the local GrevID with the server UUID;
- replace the server UUID with a GrevID;
- copy a Grev.dad password into Grev Home;
- copy a browser session cookie into Grev Home;
- grant local machine Admin because the website user is an Owner/Admin;
- grant site permissions because the local profile is a Grev Home Admin.

## Device-authorisation flow

1. Grev Home submits its GrevID, local Username/Display Name and device name to the Grev.dad link-start endpoint.
2. Grev.dad returns a short-lived random device secret plus a human approval code.
3. Grev Home keeps the device secret in Windows Credential Manager.
4. The user opens Grev.dad in a browser and signs into the website using the normal website session system.
5. The approval page shows the GrevID and device being linked.
6. The user approves or denies the request.
7. Grev Home polls with the short-lived device secret.
8. After approval, Grev.dad issues a dedicated revocable desktop access token.
9. Grev Home stores the access token in Windows Credential Manager and stores only non-secret link metadata/cache in the profile tree.

The website password never crosses this boundary.

## Data authority matrix

| Data | Authority | Grev Home behaviour |
| --- | --- | --- |
| GrevID | Local Grev Home | Never changed by Grev.dad |
| Local Username | Local Grev Home | Never changed by Grev.dad |
| Local Display Name | Local Grev Home | Independent from site display name |
| Local avatar/profile files | Local Grev Home | No automatic overwrite by website |
| Local Admin/Standard/Guest role | Local Grev Home | Never derived from site permissions |
| Grev.dad user UUID | Grev.dad | Cached as remote identity only |
| Grev.dad username | Grev.dad | Read as remote identity |
| Grev.dad display name | Grev.dad | Read as remote identity |
| Grev.dad verification/admin/owner | Grev.dad | Never grants machine permission |
| Link status | Grev.dad + local credential | Server can revoke; local client can unlink |
| Friends / requests | Grev.dad | Server authoritative |
| Blocks | Grev.dad | Server authoritative and override friendship |
| Presence | Grev.dad ephemeral state | Grev Home publishes heartbeat; expiry prevents ghost-online state |
| Playing activity | Grev Home event → Grev.dad | Server stores only after authenticated device submission |
| Social activity feed | Grev.dad | Cached locally for offline display only |

Future profile synchronisation must add new fields to this matrix before code is allowed to copy them between local and server profiles.

## Offline contract

Grev.dad is additive to the appliance.

- local profile selection/sign-in never waits for Grev.dad;
- app launching and runtime tracking never depend on a presence/activity request succeeding;
- a linked account can report `Offline` while retaining its last-known non-secret remote identity;
- friends/activity reads may use a last-known cache when the network is unavailable;
- presence is never replayed from a stale queue: it expires server-side;
- revocation/expiry prevents online API access but never deletes the local GrevID profile.

## Secret storage

Access tokens and in-progress device secrets are stored through Windows Credential Manager using targets scoped by GrevID.

They are **not** written to:

- `profile.json`;
- `Connections/GrevDad/link.json`;
- `Connections/GrevDad/cache.json`;
- logs;
- Activity Center notifications.

The server stores hashes of link device secrets and access tokens, not plaintext credentials.

## Social foundation

Friends are a server-side relationship between two Grev.dad user UUIDs. They are not controller assignments, local Grev Home users, or profile subscriptions.

The social graph supports:

- outgoing friend request;
- incoming friend request;
- accept;
- decline;
- cancel;
- remove friend;
- two-way blocking precedence;
- friend presence;
- friend activity visibility.

The eventual Friends screen is only a consumer of this model.

## Presence and activity

Presence is ephemeral:

- `online`
- `away`
- `busy`
- `offline`

Activity type uses the existing Grev.dad presence vocabulary:

- `none`
- `playing`
- `listening`
- `watching`
- `working`

Grev Home currently publishes `playing` for a managed app session and refreshes the heartbeat while relevant local/runtime sessions remain active. Server expiry ensures a crashed PC cannot remain online indefinitely.

Durable Grev Home activity uses explicit events such as:

- `app.started`
- `app.stopped`

Presence and durable activity remain separate so an old activity event can never make a user appear currently online.

## Backbone gate

Friends UI, activity-feed UI, community profile presentation and profile sync should not invent their own authentication or networking code. They must use `GrevDadAccountService` and the linked-client API contract defined here.
