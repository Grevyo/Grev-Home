# Grev Overlay, App Switcher and App Killer

Milestone 0.5 adds the first cross-app Grev Home control surface on top of the 0.4 runtime/session manager.

## Global controller shortcuts

The shipped defaults are:

- Hold **LB + RB + View** for 700 ms — direct Return Home. This bypasses the overlay and restores the Grev Home Dashboard/Login.
- Hold **LB + RB + Menu** for 450 ms — open the Grev Overlay.

These combinations are **not baked into the input engine**. They are generated into `%ProgramData%\Grev Home\Data\Input\controller-shortcuts.json` on first run and can be remapped, extended with extra buttons, given different hold times, or supplemented with alternative combinations for the same action. See `docs/CONTROLLER_SHORTCUTS.md`.

Return Home and Overlay remain separate system actions so a broken overlay never removes the direct route back to Grev Home. At least one enabled Return Home binding is always required as the controller recovery path.

## Overlay boundary

The normal Grev Home UI still uses one persistent `MainWindow`. The Grev Overlay is the one intentional extra native runtime surface because it must appear above external applications while the main shell may be hidden.

It is a single reusable transparent/topmost window, not a replacement navigation window. Internal Grev Home areas still never open chains of fullscreen windows.

## Overlay v1 actions

- Resume current app
- Switch App
- Close Current App (graceful close only)
- Running Apps
- App Killer
- Return to Grev Home

`Switch App` lists sessions owned by the central `RuntimeSessionManager`. It does not send Alt+Tab keystrokes.

## Window switching

`ProcessWindowService` enumerates visible top-level Windows windows belonging to the tracked PID/process tree for a launch session. Switching restores a minimised target window and asks Windows to bring that tracked app to the foreground.

This keeps the App Switcher tied to Grev Home launch sessions rather than exposing every internal/background Windows process.

## Close vs Force Close

### Close

Normal Close sends `WM_CLOSE` / `CloseMainWindow` to tracked top-level app windows. The 0.4 monitor continues tracking the process tree until it actually exits and then records playtime normally.

### Force Close

App Killer can terminate the tracked process tree. Because this can interrupt saves/configuration writes, Force Close is deliberately separated from the normal Running Apps controls and requires selecting the same app's Force Close action twice.

Before closing/killing a PID, Grev Home checks that the process start time belongs to the launch-session time window. This reduces the risk of acting on a recycled Windows PID.

## Ownership

Overlay, Running Apps and App Killer do not own processes themselves. They all call `RuntimeSessionManager`, which remains the source of truth for:

- LaunchSessionId
- AppID
- Primary GrevID launch context
- participant snapshot
- root PID
- discovered child PIDs
- runtime state
- elapsed time
- natural exit/failure
- playtime finalisation

Controller chord detection is similarly centralised: the runtime input service receives validated shortcut bindings from `ControllerShortcutService` and raises named system actions. Views/runtime features do not inspect specific controller-button combinations themselves.

## 0.5 smoke test

Use the Notepad registration flow from `docs/RUNTIME.md`.

1. Launch Notepad through Installed Apps and confirm Grev Home hides.
2. Use the currently configured **Overlay** shortcut and confirm the semi-transparent Grev Overlay appears above Notepad.
3. Choose Resume and confirm Notepad returns to foreground.
4. Open Overlay again and choose Return to Grev Home; confirm Notepad keeps running.
5. Open Running Apps and choose Switch; confirm Notepad returns to foreground.
6. Open Overlay and choose Close Current App; confirm a normal Notepad close is requested and Grev Home returns.
7. Relaunch Notepad, return Home, open App Killer and select Force Close once; confirm it changes to a confirmation action without killing Notepad.
8. Select CONFIRM FORCE CLOSE and confirm Notepad terminates and its runtime session disappears after the normal exit grace period.
9. Launch two registered apps and verify Overlay → Switch App can move between the two tracked sessions without using Windows Alt+Tab.
10. Change the shortcut JSON, restart Grev Home, and verify the new combinations replace the shipped defaults without changing application code.
