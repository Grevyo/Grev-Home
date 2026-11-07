# Grev Home machine settings backbone

Grev Home machine settings are internal MainWindow routes/sections. They are controller-first and do not open separate fullscreen WPF windows.

Machine controls are implemented behind Windows service classes so the same operations can later be reused by a quick-settings surface without coupling Windows APIs directly to the page.

## Audio

`AudioService` uses Windows Core Audio for the active render endpoint.

Implemented:

- current default output name
- master volume
- mute/unmute
- active render endpoint enumeration
- cycle outputs from the controller-friendly Settings UI
- request a new default endpoint for Console, Multimedia and Communications roles

Volume/mute and endpoint enumeration use standard Core Audio interfaces. Default-endpoint switching uses the Windows PolicyConfig COM interface and is therefore treated as capability-style behavior: if a Windows build rejects it, Grev Home reports the failure rather than pretending the device changed.

## Display

`DisplayService` owns primary-display mode enumeration and application.

Implemented:

- current primary resolution and refresh rate
- available Windows-reported modes
- controller-friendly previous/next mode selection
- `CDS_TEST` validation before a mode is applied
- 15-second confirmation window after applying a different mode
- explicit Keep Display / Revert actions
- automatic restoration of the previous mode when the confirmation timer expires

Display changes are applied dynamically without `CDS_UPDATEREGISTRY`; Grev Home does not write an unconfirmed display mode as the permanent Windows boot mode.

Multi-monitor topology, HDR and per-display placement are not part of this initial backbone yet.

## Wi-Fi

`WifiService` uses the native Windows WLAN API (`wlanapi.dll`) rather than parsing localized `netsh` output.

Implemented:

- Wi-Fi adapter detection
- current connection / SSID
- current saved profile name
- signal quality
- available-network discovery
- connect using an existing saved Windows Wi-Fi profile
- join a new network: open, Enhanced Open (OWE) and WPA/WPA2/WPA3 Personal
- disconnect

### Joining a new network

Choosing a network Windows has no profile for asks for its password on the controller keyboard
(Settings → Connections, the header's Wi-Fi menu hands off to it, and first-run setup has the same
step so a fresh PC can get online before any account exists).

- `WifiProfileBuilder` reads the network's advertised authentication and cipher, checks the
  password (8–63 printable ASCII characters, or a 64-digit hex key) and builds the WLAN profile
  XML.
- `WifiService.JoinAsync` saves it with `WlanSetProfile`, connects, and waits up to 25 seconds for
  Windows to report the connection.
- If the connection doesn't come up (usually a wrong password), the new profile is deleted again
  and the keyboard reopens so the password can be retyped.
- WEP and Enterprise (802.1X, for example eduroam) networks are not set up by Grev Home: they're
  listed with "use Windows Settings". Once joined there, Grev Home reconnects to them like any
  saved network.
- Hidden networks (no broadcast SSID) are not offered yet.

## Start with Windows

Settings → Power → Startup, and the first-run Startup choice, write a per-user Run entry
(`HKCU\...\Run\GrevHome`) pointing at the installed `GrevHome.exe`
(`WindowsStartupService`).

- Turning it on also clears a "disabled" choice made in Task Manager's Startup tab.
- With Windows set to sign in automatically, the PC boots straight into Grev Home.
- The uninstaller removes the entry.
- Development runs under `dotnet` can't enable it.

## Bluetooth

`BluetoothService` uses Windows device/radio APIs.

Implemented:

- Bluetooth radio detection
- radio on/off state
- request radio enable/disable
- enumerate known Bluetooth devices
- pair devices through Windows' default pairing protection level
- unpair devices

Some devices require PIN, passkey confirmation or manufacturer-specific pairing flows. Those cases may need a richer future pairing modal; Grev Home reports Windows pairing results rather than assuming every Bluetooth device follows the same flow.

## Existing machine status and power

The earlier machine backbone remains in Settings:

- Windows/machine status
- CPU/logical processor count
- RAM and uptime
- power source / system battery where present
- drive/storage state
- XInput controller/battery state
- Sleep
- Restart
- Shut Down

Power actions retain the two-stage confirmation model.

## Navigation/focus ownership

The permanent MainWindow shell owns normal route landing, scroll reset and Back focus restoration.

Machine settings sections may retain local focus when expanding/collapsing a section or opening a local action, but they do not create their own route-level focus pass.

## Physical validation still required

Windows API success and CI compilation are not substitutes for hardware testing. The following need validation on the actual Grev Machine before this area is treated as release-proven:

- correct speaker/headset names and volume behavior
- default output switching on the installed Windows build
- resolution/refresh switching and 15-second rollback on the target TV/monitor
- Wi-Fi adapter/network reporting and saved-profile reconnect
- Bluetooth radio access and controller/headset pairing behavior
- controller navigation through every dynamic network/device row
