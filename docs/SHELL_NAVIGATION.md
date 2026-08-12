# Grev Home Shell & Navigation — Milestone 0.15

Milestone 0.15 freezes the permanent-window shell and controller navigation contract before further package catalogue expansion.

The goal is that every Grev Home surface behaves like one console UI rather than a collection of independent WPF pages.

## Permanent shell boundary

Normal Grev Home navigation stays inside the permanent `MainWindow` shell.

- route content is hosted in `RouteHost`;
- the header remains persistent;
- normal pages do not create separate maximized native Windows;
- the existing external-app overlay remains the deliberate exception for runtime control/help;
- modal confirmation/help surfaces must overlay the current shell rather than replacing it with another fullscreen Window.

## Route transition contract

`NavigationService` now distinguishes:

- `Reset` — replace the navigation root and clear history;
- `Forward` — push the current content and enter new content;
- `Back` — restore the previous content from history;
- `SameRoutePush` — add modal/action-menu history without visually changing the route enum;
- `SameRouteBack` — close that same-route modal/action-menu history entry without treating the parent as a fresh page.

Normal cross-route navigation uses `Navigate(route)`.

Same-route **modal** history uses `Navigate(route, allowSameRoute: true)`. This is reserved for overlays/action menus where the route itself does not change but B/Back must close the overlay before leaving the parent page.

Same-route **content** navigation uses `NavigateWithinRoute(route)`. This is for real child content that happens to share one route enum, such as entering another Files directory. It behaves like normal Forward/Back navigation: the child starts at the top and Back restores the exact parent history entry.

`RouteChanging` fires while the outgoing route/content is still presented. This lets the shell capture controller focus state before page content changes.

`RouteChanged` remains the existing compatibility event used by page integrations.

## Focus contract

Controller focus is shell-owned at route boundaries.

### Forward / Reset

Fresh content opens predictably:

1. visible route ScrollViewers start at the top/left;
2. first enabled visible controller-selectable Button receives focus;
3. header navigation can still be reached by moving up from the top route row.

The shell deliberately bookmarks Buttons rather than arbitrary `Focusable` WPF elements. A ScrollViewer, text surface or other implementation detail must never become the accidental controller landing target.

Fresh content must not inherit an arbitrary old scroll position merely because its persistent WPF view object was previously visited.

### Back

Back means "return to where I was" rather than "reopen the page from the beginning".

Focus is stored **per navigation history entry**, not merely per Route enum. This matters for nested same-route content such as Files directories and for multiple stacked child pages.

When a Forward or SameRoutePush history entry is created, the shell records:

- the actual focused Button through a weak reference;
- that Button's index among current controller-selectable route Buttons;
- the navigation history depth that owns that bookmark.

When Back or SameRouteBack returns:

1. the matching history-entry bookmark is removed and restored;
2. the existing parent viewport is preserved;
3. the same Button is focused if it still exists;
4. if a dynamic list rebuilt its Buttons, the shell falls back to the same controller-selectable Button index;
5. only if neither is possible does it fall back to the first route Button.

This is especially important for Store/library/profile/file grids where returning from detail/child content should not dump the user back onto the first tile.

### Same-route modal history

A `SameRoutePush` does not apply fresh-page scroll/focus rules. The modal/action-menu integration owns its own initial controller focus while open.

`SameRouteBack` restores the parent history-entry bookmark. The shell deliberately does not capture the modal's currently focused button while closing it, so a Cancel/confirm button can never overwrite the parent tile/action that originally opened the modal.

Mouse/keyboard modal completion must use the same Back transition as controller B where possible. Discarding history silently is not enough because it skips parent focus restoration.

## Back button contract

The persistent shell Back button reflects navigation history rather than page-specific guesswork.

- Dashboard/root normally has no Back action.
- Forward routes/content have Back when history exists.
- Login retains its signed-in escape to Dashboard for legacy/reset cases.
- modal/action-menu history may temporarily add one same-route Back entry.
- nested Files directories create normal Forward history even though they share `Route.Files`.

Page-specific Back buttons and controller B/Escape must call the same navigation/history behavior; they must not invent unrelated route destinations.

## Scroll contract

Dynamic text/content must use wrapping and Auto/MinHeight rather than fixed heights that can clip.

Route scrolling follows two rules:

- **fresh Forward/Reset** -> top/left;
- **Back/SameRouteBack** -> preserve the parent route/content viewport.

Focus and scrolling are separate concerns. Focusable footer actions must not live inside a help-content ScrollViewer when focusing them would drag the help content away from its start. The controller-guide redesign is the reference pattern: scrollable information plus a fixed action footer.

## Modal contract

All shell modals should converge on one reusable behaviour:

- current route remains visually underneath;
- interaction underneath is not the controller focus target while the modal is open;
- modal has a bounded responsive card, not a fixed giant surface;
- dynamic text wraps;
- content may scroll independently when necessary;
- action footer remains fixed where practical;
- actions are horizontal/side-by-side when width permits;
- safe/cancel action receives initial controller focus for destructive confirmations;
- B/Escape closes/cancels where safe;
- opening/closing a modal must not corrupt route history or parent-route focus.

Reusable application resources now include:

- `ShellModalCardStyle`;
- `ShellModalActionButtonStyle`;
- `ShellModalDangerActionButtonStyle`;
- `ShellModalEyebrowStyle`.

Normalized 0.15 modal surfaces so far:

- Store install progress;
- Store uninstall warning;
- Installed Apps action menu;
- Files rename/create editor;
- Files delete confirmation;
- external-app controller guide from the preceding controller-guide redesign.

The header Power/System menu intentionally remains a compact top-right flyout rather than pretending every popup has the same physical composition.

## Files same-route rules

`Route.Files` contains both real child content and in-page modals, so it is the reference case for the two same-route navigation modes.

### Entering Home/folder/parent content

Files pushes its own path state, then calls `NavigateWithinRoute(Route.Files)`.

The shell treats that as normal Forward:

- new directory begins at the top;
- first controller Button receives initial focus;
- Back restores the previous directory and its matching history-entry focus bookmark.

### Rename/create/delete modal

Files calls `Navigate(Route.Files, allowSameRoute: true)`.

- rename/create explicitly focuses the first controller keyboard key;
- delete explicitly focuses Cancel;
- the underlying Files toolbar does not become the modal landing target;
- controller B closes the modal through `SameRouteBack`;
- modal Cancel/save/delete completion also returns through the matching navigation Back entry so the originating Files control is restored.

## Route audit inventory

Current routes:

1. Login
2. Create Profile
3. Dashboard
4. Profile Players
5. Profile View
6. Profile Edit
7. Profile Photo Picker
8. Grev Store
9. Grev Store App
10. Installed Library
11. App Settings
12. Running Apps
13. App Killer
14. Settings
15. Admin Console
16. Files

Every route must be checked for:

- controller entry focus;
- four-direction navigation;
- route-to-header navigation;
- Back behavior;
- scroll-at-entry behavior;
- Back focus/viewport restoration;
- dynamic text wrapping/no clipping;
- empty-state focus behavior;
- modal interaction;
- mouse/keyboard parity;
- no separate fullscreen WPF navigation Window.

## 0.15 implementation order

### Pass 1 — route/focus foundation

Implemented:

- transition kinds and pre-change event;
- shell Back-state ownership;
- per-history-entry Button focus bookmarks;
- fresh-route scroll reset;
- Back/SameRouteBack focus restoration;
- dynamic-list focus-index fallback;
- separate same-route content and modal history semantics.

### Pass 2 — modal normalization

Implemented/under physical validation:

- common shell-modal layout/styles;
- fixed action footers where needed;
- controller-safe modal focus/Back contract;
- compact horizontal/wrapped action rows;
- Store, Installed Apps and Files popup cleanup.

### Pass 3 — route-by-route controller audit

Walk every route physically with controller and keyboard, fixing directional dead ends and inconsistent Back behavior.

Static audit should avoid dragging Profile-specific layout/detail work into 0.15; deeper Profile/Accounts surfaces belong to Milestone 0.16.

### Pass 4 — shell/header/dashboard finalisation

Finalize dashboard ordering/content, persistent header behaviour and any remaining root-navigation decisions before moving to Milestone 0.16 Profiles/Accounts.

## Physical validation boundary

A green CI build proves only that the WPF code compiles. Controller focus geometry, scroll landing and route restoration are not considered physically validated until tested in the running Grev Home UI.
