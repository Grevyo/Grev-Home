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
- `Forward` — push the current route and enter a new route;
- `Back` — restore the previous route from history;
- `SameRoutePush` — add a back entry without visually leaving the route, used by modal/action-menu history patterns.

`RouteChanging` fires while the outgoing route is still presented. This lets the shell capture controller focus/viewport state before page content changes.

`RouteChanged` remains the existing compatibility event used by page integrations.

## Focus contract

Controller focus is shell-owned at route boundaries.

### Forward / Reset

A fresh page opens predictably:

1. route viewport starts at the top/left;
2. first enabled visible focusable route control receives focus;
3. header navigation can still be reached by moving up from the top route row.

A fresh page must never inherit an arbitrary old scroll position merely because its persistent WPF view object was previously visited.

### Back

Back means "return to where I was" rather than "reopen the page from the beginning".

Before leaving a route the shell records:

- the actual focused route control through a weak reference;
- that control's index among current focusable route elements.

When Back returns:

1. the existing route viewport is preserved;
2. the same control is focused if it still exists;
3. if a dynamic list rebuilt its buttons, the shell falls back to the same focusable index;
4. only if neither is possible does it fall back to the first focusable route control.

This is especially important for Store/library/profile grids where returning from a detail page should not dump the user back onto the first tile.

### Same-route modal history

A same-route push does not apply fresh-page focus/scroll rules. The modal/action-menu integration owns its modal focus while open. When Back closes the modal, the previously captured route bookmark can be restored.

## Back button contract

The persistent shell Back button reflects navigation history rather than page-specific guesswork.

- Dashboard/root normally has no Back action.
- Forward routes have Back when history exists.
- Login retains its signed-in escape to Dashboard for legacy/reset cases.
- modal/action-menu history may temporarily add one same-route Back entry.

Page-specific Back buttons and controller B/Escape must call the same navigation/history behavior; they must not invent unrelated route destinations.

## Scroll contract

Dynamic text/content must use wrapping and Auto/MinHeight rather than fixed heights that can clip.

Route scrolling follows two rules:

- **fresh Forward/Reset** -> top/left;
- **Back** -> preserve the route's existing viewport.

Focus and scrolling are separate concerns. Focusable footer actions must not live inside a help-content ScrollViewer when focusing them would drag the help content away from its start. The controller-guide redesign is the reference pattern: scrollable information plus a fixed action footer.

## Modal contract

All shell modals should converge on one reusable behaviour:

- current route remains visually underneath;
- shell interaction underneath is disabled;
- modal has a bounded responsive card, not a fixed giant surface;
- dynamic text wraps;
- content may scroll independently when necessary;
- action footer remains fixed where practical;
- actions are horizontal/side-by-side when width permits;
- safe/cancel action receives initial controller focus for destructive confirmations;
- B/Escape closes/cancels where safe;
- opening/closing a modal must not corrupt route history or parent-route focus.

Current modal surfaces to normalize during 0.15:

- header Power/System menu;
- Store install progress;
- Store uninstall warning;
- Installed Apps action menu;
- profile/edit keyboards/pickers where they behave modally;
- external-app controller guide in the existing overlay window.

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

- transition kinds and pre-change event;
- shell Back-state ownership;
- focus bookmarks;
- fresh-route scroll reset;
- Back focus restoration;
- dynamic-list focus-index fallback.

### Pass 2 — modal normalization

- common shell-modal layout/styles;
- fixed action footers where needed;
- one controller-safe modal focus/Back contract;
- eliminate page-specific popup quirks.

### Pass 3 — route-by-route controller audit

Walk every route physically with controller and keyboard, fixing directional dead ends and inconsistent Back behavior.

### Pass 4 — shell/header/dashboard finalisation

Finalize dashboard ordering/content, persistent header behaviour and any remaining root-navigation decisions before moving to Milestone 0.16 Profiles/Accounts.

## Physical validation boundary

A green CI build proves only that the WPF code compiles. Controller focus geometry, scroll landing and route restoration are not considered physically validated until tested in the running Grev Home UI.
