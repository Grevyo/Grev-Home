# Shell presentation and motion

Grev Home owns startup and route motion at the permanent shell boundary. Individual pages do not
create extra windows or independent transition systems.

## Startup intro

The startup sequence is a programmed WPF surface, not a video dependency. It presents the Grev
mark and name while normal local data/profile startup continues behind it, blocks accidental input,
then restores controller focus to the active route. Theme & Motion includes a controller-accessible
preview action.

## Screen transitions

Normal forward, back and reset route changes fade and move the incoming route over roughly 300 ms.
Same-route action-menu history is excluded so modal controller interactions remain immediate.
Settings hub/detail changes use the same enabled preference.

Both startup intro and screen transitions default to enabled and can be switched independently in
Settings → Theme & Motion. Preferences are machine-wide because the intro runs before a Primary
GrevID is selected. They are persisted atomically in `Data\Presentation\shell-motion.json`.

Disabling transitions immediately clears any active route animation. Disabling the intro applies to
the next Grev Home launch; Preview startup intro remains available without changing that choice.
