# Grev Home 1.0 test checklist

Work through this on a real Windows PC with a controller, preferably with **no keyboard or mouse
plugged in** for anything marked 🎮. Items marked ★ are new or changed since the last tested build
and have never run on real hardware, so test those first if time is short.

For each item, note **Pass / Fail / Didn't test**, and for a failure what you did, what you expected
and what happened (a photo of the screen helps). Crash details land in
`C:\GrevCo\GrevHome\Logs\grevhome-crash.log`; attach it if Grev Home closes unexpectedly.

## What you need

- A Windows 10 or 11 PC you can reinstall Grev Home on. Ideally also a second PC or a spare Windows
  user account, for sync and restore tests.
- An Xbox-style controller. A second controller helps with multi-user tests.
- A Wi-Fi network with a password that the PC has **never joined**. A phone hotspot works. Optionally,
  an open network.
- Two grev.dad accounts that are friends, or that you can make friends during the test, plus a
  browser on another device.
- A couple of games or ROMs you own, and BIOS files where an emulator needs them.

Before starting, merge grev-dad-site [PR #506](https://github.com/Grevyo/grev-dad-site/pull/506)
into **pbe** and test against pbe.grev.dad, or merge to main for production. Then build or download
the installer from Grev-Home [PR #18](https://github.com/Grevyo/Grev-Home/pull/18).

---

## 1. Website

- [ ] ★ grev.dad/login shows **"Looking for the Grev Home download?"** with the Grev Home logo,
  under "No account yet?".
- [ ] ★ The same link is on grev.dad/signup.
- [ ] ★ Clicking it opens the latest Grev Home release on GitHub in a new tab.
- [ ] ★ It looks right on a phone: the logo isn't squashed and the text wraps.
- [ ] Signing up, logging in, "keep me signed in" and logging out still work.

## 2. Install, update and uninstall

- [ ] 🎮 The installer runs start to finish with only the controller (D-pad, A, B), including
  folder choices.
- [ ] Windows SmartScreen shows "unknown publisher". That's expected until a signing certificate is
  added; note it, it isn't a failure.
- [ ] Grev Home launches after install, fullscreen, with no desktop showing.
- [ ] Running the installer again over the top updates without losing your Games/BIOS folder
  choices.
- [ ] The in-app update check, if a newer release exists, downloads and installs.
- [ ] ★ Uninstalling removes Grev Home. After a restart, Windows doesn't try to start it (no error).
  Your games, saves and `C:\GrevCo\GrevHome\Profiles` data are kept.

## 3. First-run setup (fresh install, no profiles yet)

- [ ] 🎮 The welcome screen can be used fully with the controller.
- [ ] Games and BIOS folder choices save, and "Custom Location…" uses the controller keyboard.
- [ ] ★ **Network:** the status line correctly says whether you're on Wi-Fi.
- [ ] ★ 🎮 "Find Wi-Fi networks" lists nearby networks with signal strength and type (saved,
  password, open, use Windows Settings).
- [ ] ★ 🎮 Choosing the **never-joined password network** opens the controller keyboard.
  - [ ] The password is hidden as you type.
  - [ ] Entering the right password connects within about 25 seconds, and it says "✓ Connected to …".
- [ ] ★ A wrong password says "Couldn't connect … check the password", and nothing is left behind:
  the network doesn't appear as saved in Windows.
- [ ] ★ An enterprise or work network, if one is around, says to use Windows Settings instead of
  trying.
- [ ] ★ **Startup:** "Start Grev Home when Windows starts" is ticked by default and can be toggled.
- [ ] Continue goes on to creating the first account.

## 4. Start with Windows

- [ ] ★ With the first-run Startup choice ticked, restart the PC and sign in: Grev Home opens by
  itself.
- [ ] ★ Settings → Power → Startup shows "Start with Windows: On". Turn it Off, restart: Grev Home
  doesn't open.
- [ ] ★ Turn it back On, restart: it opens again.
- [ ] ★ Disable it in Task Manager → Startup apps, then check Settings: it shows Off. Turning it On
  in Grev Home re-enables it.
- [ ] ★ Optional, for the true console experience: turn on automatic sign-in in Windows, restart,
  and the PC goes straight into Grev Home with no keyboard needed.

## 5. Accounts, login and sessions

- [ ] 🎮 The login screen is always the first screen. Choose a profile with the controller.
- [ ] Create a local profile: GrevID and username are shown; display name can be changed later.
- [ ] Guest sign-in works and leaves nothing behind afterwards.
- [ ] A controller password (if set) is required at sign-in and can be removed.
- [ ] A second controller can sign in a second user, and Primary User switching works.
- [ ] Logging out returns to the login screen.

## 6. Grev.dad linking and recovery

- [ ] Edit Profile → Grev.dad: "Generate approval code" → approve on the website → Grev Home
  notices without pressing anything.
- [ ] Linking during first account creation, in the onboarding step, also works.
- [ ] ★ After linking, the grev.dad profile comes over: display name, bio, avatar and banner match
  the website.
- [ ] Reinstall Grev Home, or use the second PC, and link the same grev.dad account: level,
  achievements, stats and cloud saves are restored.
- [ ] Unlinking works, and the profile keeps working offline.
- [ ] With the internet off, Grev Home still works, and Friends shows cached data marked offline.

## 7. Profiles (★ all new)

**Your profile page**
- [ ] The **Profile space** section shows your tiles, with the mini profile card where your grev.dad
  card sits.
- [ ] 🎮 D-pad moves tile by tile, and the page scrolls with focus.
- [ ] Widgets show real data:
  - [ ] Recent games, game activity (with "Now playing" while a game runs), most played.
  - [ ] Stats, achievements, bio.
  - [ ] Favourite games, best friends, RetroAchievements.
- [ ] Unlinked or offline: the tiles still show, and the widgets use this PC's history.

**Editing tiles**
- [ ] 🎮 "Edit Tiles" (or Edit Profile → Profile Tiles) opens the controller editor.
- [ ] 🎮 A on an empty cell → choose type; "Live Widget…" jumps to the widgets; every widget can be
  added.
- [ ] 🎮 Y on a widget → "Items shown" changes with Left/Right; Save.
- [ ] The grev.dad profile card area is drawn and tiles can't be moved into it.
- [ ] Save syncs: the same tiles and widgets appear on your grev.dad profile page.
- [ ] Edit tiles on the website, then reopen your Grev Home profile: the changes are there.

**Identity sync**
- [ ] Change bio or avatar in Grev Home → it shows on grev.dad.
- [ ] Change them on grev.dad → reopen your Grev Home profile → it updates.
- [ ] A long bio (over 160 characters, with line breaks) from grev.dad shows in full in Grev Home
  and isn't cut down.
- [ ] Hide your bio or avatar in grev.dad's profile privacy → your friend no longer sees it in
  Grev Home.

**Favourites and RetroAchievements**
- [ ] A game's settings page → "☆ Add to favourite games" → it appears in the Favourite games widget
  (Grev Home and grev.dad).
- [ ] Starring a game while offline syncs once you're back online.
- [ ] Edit Profile → RetroAchievements → enter your username → the widget shows points, rank and
  recent unlocks. Needs the RetroAchievements key set on the server.
- [ ] A made-up username is refused, and Unlink works.

## 8. Friends and messaging

- [ ] Add a friend by friend code; accept, decline and cancel requests.
- [ ] ★ Friend cards (mini profiles) show avatar, name, headline, "🎮 Playing …" when they're in a
  game, and stats they chose to share.
- [ ] ★ Open a friend: their tiles and widgets show. Their private game sessions don't.
- [ ] ★ "☆ Add to best friends" → ★, and they move to the top of your friends list and into your Best
  friends widget.
- [ ] ★ 🎮 Choosing a friend inside a Best friends widget opens their profile.
- [ ] Messaging: send, receive, unread counts, and older messages.
- [ ] Removing a friend works.

## 9. Library, games and the Store

- [ ] Game scanning finds games in your Games folder.
- [ ] Adding a game manually, game settings (name, logo, tile art, background) and internet artwork
  scraping all work.
- [ ] Launching a game hides Grev Home; quitting the game returns to where you were. Play time is
  recorded on the profile.
- [ ] 🎮 Return Home (LB + RB + View, hold) and the Grev Overlay (LB + RB + Menu) work during a game.
- [ ] App Switcher, Running Apps and App Killer (two-step force close) work.
- [ ] Store installs run silently, each with controller defaults set, and launch correctly:
  - [ ] RetroArch, PCSX2
  - [ ] Dolphin, Cemu, RPCS3, Azahar, xemu, Xenia Canary, Vita3K
  - [ ] Steam, Discord, Kodi, Plex HTPC, Jellyfin Media Player, Stremio, GrevReader
- [ ] Emulators that need BIOS or firmware say so clearly and pick it up from the BIOS folder.
- [ ] Store apps don't add themselves to Windows startup.

## 10. Cloud saves

- [ ] Turn cloud saves on for a game or emulator → play and save → quit → it uploads.
- [ ] On the second PC or after a reinstall, the save downloads before the first launch.
- [ ] A save conflict (played on both PCs without syncing) stops the launch and asks **Keep This Device** or **Use Cloud**; nothing is overwritten silently.
- [ ] Cloud saves don't block playing while offline.

## 11. Settings

- [ ] Account: display name change with the controller keyboard.
- [ ] Controller shortcuts: record a new combination; Return Home always keeps at least one binding.
- [ ] Audio, display, system information and game scanning settings.
- [ ] ★ Connections: Wi-Fi list shows saved, password and open networks.
  - [ ] ★ 🎮 Joining a new password network from here works the same as first-run, and a wrong
    password reopens the keyboard.
  - [ ] Disconnect works.
- [ ] ★ The header's quick Wi-Fi menu: saved networks connect directly; a new network ("Set up")
  opens Settings → Connections.
- [ ] Bluetooth: turn on and off, pair and unpair a controller or headset.
- [ ] Power: sleep, restart and shut down need a second press to confirm.

## 12. Themes and Home layouts

- [ ] Every preset applies and looks right: PS5, PS4, Xbox 360 blades, Xbox 360 tabs, Xbox One, Wii,
  Switch, plus the original Classic.
- [ ] 🎮 In each layout you can reach every tile and section with the D-pad. Tabs and blades switch
  sections.
- [ ] Theme Creator: start from a preset, change colours, tile shape, size, spacing, position, focus
  effect, font, background and dock/clock; the live preview updates; save and apply.
- [ ] Previewing a theme then backing out restores the previous theme.
- [ ] Profile themes vs machine themes: each profile keeps its own, and shared themes show for
  everyone.

## 13. Stability

- [ ] Leave Grev Home on Home for an hour, then open a few screens: still responsive.
- [ ] Sleep and wake the PC: the controller still works and Grev Home is still in charge.
- [ ] Unplug and replug the controller mid-use.
- [ ] Pull the network cable or turn Wi-Fi off mid-sync: no crash, and it syncs later.
- [ ] No crash log was written during the whole test.

---

## Release, once everything above passes

1. Add the code-signing certificate secrets (`docs/INSTALLER.md` → Code signing).
2. Set the `RETROACHIEVEMENTS_API_KEY` Worker secret for production.
3. Merge grev-dad-site #506 to main (this deploys production) and Grev-Home #18 to main.
4. Bump the version to **1.0.0** in `src/GrevHome/GrevHome.csproj`,
   `installer/Launcher/GrevHome.Installer.csproj` and `installer/GrevHome.iss`, then merge. CI
   publishes the GitHub release, and the website download link points to it.
5. Remove the "LIVE BETA" banner from grev.dad if 1.0 means the site too.

## Reporting back

Copy this per failure:

```
Section / item:
What I did:
What I expected:
What happened:
Controller only? (y/n)
Crash log attached? (y/n)
```
