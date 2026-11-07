# Grev Home installer

Grev Home ships as a standard Windows Setup .exe built with Inno Setup. Someone installing it downloads one file, runs it, clicks through a normal wizard, and gets a Start Menu entry and (optionally) a desktop shortcut. No .NET runtime install is required separately - the publish step below bundles it.

## Building the installer

Requirements: the .NET SDK, and [Inno Setup 6](https://jrsoftware.org/isdl.php) installed (or its `ISCC.exe` on `PATH`).

```powershell
installer\build-installer.ps1
```

This does two things:

1. `dotnet publish` builds a self-contained `win-x64` copy of Grev Home into `publish\`.
2. Inno Setup (`installer\GrevHome.iss`) packages `publish\` into `dist\GrevHomeSetup.exe`.

`GrevHomeSetup.exe` is the file to hand out - copy it, upload it, whatever. Neither `publish\` nor `dist\` is committed to the repository; both are build output.

## What the installer does

- Installs to `C:\GrevCo\GrevHome` by default. `C:\GrevCo` is the shared home for Grev software, with each product in its own subfolder.
- Grev Home now uses that same `C:\GrevCo\GrevHome` root for all Grev-owned runtime content: `Data`, `Profiles`, `Global`, `Packages`, `Themes`, `Downloads`, `Logs`, and the default `Games` and `Bios` folders.
- Runs without admin elevation (`PrivilegesRequired=lowest`) because the default GrevCo folder is writable without installing into Program Files.
- Creates a Start Menu group and, if the user opts in, a desktop shortcut.
- Detects a running Grev Home instance (via the same named mutex the app already uses for single-instance enforcement) and asks the user to close it before installing or uninstalling over it, rather than failing partway through.
- Setup only removes files it installed. Runtime-created Grev Home data under the Grev Home root is not tracked as an installer payload and is preserved unless the user removes it separately.

Third-party software that is intentionally system-installed, such as Microsoft WebView2, Visual C++ runtimes, Steam or Discord, may still use the vendor's normal Windows locations. Grev Home-owned files and portable Grev Store packages stay under the Grev Home root.

## First run

The installer first offers independent PC games, apps and emulator/console-game choices, so any combination can be selected. When emulator support is selected it asks which consoles should be prepared and whether Games/BIOS folders already exist. It offers `C:\GrevCo\GrevHome\Games` and `C:\GrevCo\GrevHome\bios` as the standard locations and passes those selections into the controller-first setup shown on Grev Home's first launch. BIOS setup is omitted when no emulator console was selected.

## Code signing

Without a signature, Windows SmartScreen shows "Windows protected your PC / unknown publisher"
when people run the installer, and again for updates. To sign:

1. Buy a code-signing certificate. An OV certificate works; an EV certificate, or Azure Trusted
   Signing, builds SmartScreen reputation faster.
2. Export it as a password-protected `.pfx`, then base64-encode it:
   `[Convert]::ToBase64String([IO.File]::ReadAllBytes("grevhome.pfx")) | Set-Clipboard`
3. In GitHub → repository Settings → Secrets and variables → Actions, add
   `GREVHOME_SIGNING_CERT_BASE64` (the base64 text) and `GREVHOME_SIGNING_CERT_PASSWORD`.

`installer/build-installer.ps1` then:

- signs `GrevHome.exe`, the embedded Inno Setup engine and `GrevHomeSetup.exe` with SHA-256 and a
  DigiCert timestamp;
- checks each signature;
- writes the published SHA-256 after signing, so the in-app updater's checksum still matches.

Without the secrets the build is unchanged and unsigned. To sign locally, set the same two
environment variables before running the script.
