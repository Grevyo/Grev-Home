# Dashboard destination presentation

Every fixed Home destination has a stable presentation ID which is independent from its route and
click action. The shipped dashboard supplies the default name, detail and colour. A persistent
GrevID may override the name, background colour and full-tile PNG/JPG/BMP/GIF under:

```text
Profiles/<GrevID>/Presentation/Dashboard/<tile-id>/
```

The supported IDs are `my-profile`, `users-controllers`, `your-games`, `add-game`,
`installed-apps`, `grev-store`, `files`, `grev-dad`, `web-browser`, `running-apps`,
`activity-center`, `app-killer`, `settings`, the `settings-*` shortcuts and `admin-console`.

Home groups these into four rows: Account (`my-profile`, `users-controllers`), Apps & Content,
Friends (conditional and online-only) and System. Every `settings-*` ID resolves to a real settings
page through `SettingsView.ResolveSettingsPage`, which is the single source of truth for that
mapping so a catalogued tile can never become a dead button. The regression test asserts it.

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

## Tile motion

Hover, focus, press and reveal motion for every tile surface lives in `ShellTileMotion`. Dashboard,
Settings hub, Grev Store, Installed Apps and game tiles all attach to it rather than animating
buttons themselves, so a mouse hover and a controller focus produce the same lift, scale and accent
glow. The helper owns an attached tile's `RenderTransform`; a caller must not assign its own.

Three machine settings under Theme & Motion govern it, and each keeps its own meaning:

- **Tile hover effects** — mouse hover lift, scale and glow.
- **Tile focus animation** — the same treatment driven by controller/keyboard focus.
- **Tile reveal animation** — the staggered fade/rise when a row is populated or Home is reopened.

Turning motion off must never hide a tile or leave one mid-transform: tiles are returned to rest at
full opacity with no residual effect, and the focus border continues to carry focus visibility.
Animation speed (Relaxed/Normal/Fast) scales every duration, including the reveal stagger.
