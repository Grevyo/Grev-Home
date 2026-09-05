# Dashboard destination presentation

Every fixed Home destination has a stable presentation ID which is independent from its route and
click action. The shipped dashboard supplies the default name, detail and colour. A persistent
GrevID may override the name, background colour and full-tile PNG/JPG/BMP/GIF under:

```text
Profiles/<GrevID>/Presentation/Dashboard/<tile-id>/
```

The supported IDs are `your-games`, `installed-apps`, `grev-store`, `files`, `running-apps`,
`activity-center`, `app-killer`, `settings` and `admin-console`.

Right-click or hold controller A on a destination tile to open its controller-first appearance
settings. Uploaded artwork is copied into the profile and is reusable on another Home destination.
Reset deletes only that destination's override and reveals the active theme/shipped default again.

Theme packages will own shared layout, typography, surfaces, focus treatment, dimensions and the
default presentation of these IDs. A theme must never own or overwrite per-GrevID dashboard
overrides. Resolution is always:

```text
shipped fallback -> active theme default -> GrevID dashboard override
```

Continue/Recent entries are dynamic app/game content rather than fixed destinations. They continue
to resolve through the owning app/game presentation contract, so the same custom artwork appears
there without duplicating dashboard overrides.

Shipped destination defaults use distinct built-in vector symbols rather than the shared neutral
missing-artwork mark. These vectors remain available offline and can be replaced by theme defaults
or a GrevID override.

The Friends dashboard section and header action are conditional online surfaces. They are hidden
for Unlinked, Linking, Expired, Revoked and Error states, and shown only for Linked or Offline state
where a previously valid Grev.dad connection exists. Offline state may display the last safe cached
friends snapshot; it never blocks local Home navigation.
