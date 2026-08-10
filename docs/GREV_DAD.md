# Grev.dad account connection model

Grev.dad is **not a separate Grev Home login type**.

A persistent Grev Home user starts as a local account with its own immutable GrevID and Username. That local account can later be linked to a Grev.dad account, adding online identity/sync/community capabilities without replacing the local identity.

## Core model

```text
Local Grev Home account
  GrevID
  Username
  DisplayName
  local profile data
        |
        +-- optional Grev.dad connection
                account/provider identity
                authentication tokens
                sync/community state
```

Linking Grev.dad must not:

- create a second local profile;
- change the GrevID;
- change the immutable local Username automatically;
- rename/move the profile folder;
- make local sign-in depend on internet access;
- destroy local data when the connection is removed or unavailable.

## Login behavior

The Grev Home login / `Who's playing?` screen presents:

- persistent local profiles;
- Guest.

It does not present Grev.dad as a third account type.

A local profile that has been linked to Grev.dad may show a connected/badged state on its existing profile card later, but it remains the same local Grev Home account.

## Multiple users

Each persistent local Grev Home account can independently link its own Grev.dad identity. One machine may therefore contain several local GrevIDs with different Grev.dad connections.

Guest does not become a persistent Grev.dad-linked identity.

## Offline behavior

Grev Home must remain usable when Grev.dad is unavailable or the machine is offline. The local profile, controller assignment, Primary User selection, local apps/data and Grev Home runtime continue to function from local state.

Online features should surface connection/sync errors without blocking normal local use.

## Future cross-machine behavior

A future Grev.dad sign-in/recovery flow may help locate, restore or import a user's existing Grev Home identity onto another machine. That flow must resolve identity conflicts explicitly and preserve the portable GrevID contract rather than silently creating duplicate identities.

The exact sync/recovery design belongs to the later Account Connections milestone.
