# Grev Home

Grev Home is a controller-first Windows console shell designed to make a PC usable from boot without requiring a keyboard or mouse. Keyboard and mouse remain fully supported, but every core feature is designed around controller operation first.

## Product rules

- The first screen is always account login / user selection.
- A session will support multiple signed-in users and controller assignments, with one primary user owning profile-specific app data while all session participants can receive playtime.
- Grev Home uses one persistent fullscreen shell rather than opening separate fullscreen windows for each area.
- Home, Library, Store, Files, Running Apps, App Killer, Settings and future features share one dashboard-style navigation system.
- Local and Guest accounts work without an online dependency. Grev.dad is an optional account / sync / community layer.
- Apps are described by a generic app definition instead of being hard-coded into the UI.
- Grev Home remains resident while launched apps run so a controller shortcut can always restore the shell and later open the Grev Overlay / app switcher.
- Grev Store will eventually distribute apps, emulators, themes, add-ons and community packages.
- Themes are portable packages that can be created, exported, imported and eventually shared through Grev Store.

## Milestone 0.1

The first milestone intentionally proves only the foundation:

1. Single fullscreen borderless WPF shell.
2. Login is always the initial route.
3. Local and Guest placeholder sign-in flows.
4. Dashboard-style Home surface.
5. Shared keyboard, mouse and XInput controller navigation.
6. Controller shortcut to restore Grev Home when it is not focused.
7. Logout / back to Login without creating another Window.

No Store, installer, emulator, theme, file-management or online-account implementation belongs in 0.1.

### Controls

- D-pad / left stick or arrow keys — move focus
- A or Enter / Space — select
- B or Escape / Backspace — back
- Hold **LB + RB + View** for 700 ms — restore Grev Home and return to Dashboard/Login

## Stack

- Windows 10/11
- .NET 10
- WPF
- XInput as the initial controller backend

## Run

```powershell
dotnet run --project .\src\GrevHome\GrevHome.csproj
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the decisions that future milestones must preserve.
