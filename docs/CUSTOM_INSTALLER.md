# Grev Home custom installer

Grev Home 0.16 replaces the visible stock setup wizard with a controller-first WPF experience. The established Inno Setup package remains embedded as a silent engine so upgrades, shortcuts, WebView2 installation and uninstall registration retain their existing behaviour.

## Brand roles

- `GrevHomeLogo.png` is the main product identity. It is used in the installer header, animated hero and installation state.
- `GrevMark.png` is the personal Grev identity. It appears as the creator signature.
- `GrevCo.jpeg` is the company identity. It appears as a stable signature card in the installer rail.

The source assets live in `installer/Launcher/Assets` and are compiled into the single installer executable.

## Input

Every action is available by mouse and keyboard. XInput controllers support D-pad or left-stick focus movement, A to select, and B to go back. Focus remains visibly outlined throughout the flow.

## Setup choices

The installer accepts any combination of PC games, apps and emulators. Emulator users can select PlayStation, PlayStation 2, PlayStation 3, Nintendo DS, Nintendo Switch, Nintendo 3DS and Original Xbox. These selections prepare first-run preferences and folders; they do not imply that every emulator is already installed or configured.

Games and BIOS each provide the same two choices:

- Use my existing folder (browse to directory).
- Use the Grev Home standard under `C:\GrevCo\GrevHome`.

Grev Home never downloads BIOS or firmware files.

When a standard location is selected, the installer visibly warns that the user must move their own legally obtained ROMs and ISOs into the Games folder, and legally obtained BIOS or firmware files from hardware they own into the BIOS folder. Grev Home does not provide these files.

## Packaging

Run `installer\build-installer.ps1` on Windows. It publishes Grev Home, downloads and validates Microsoft's WebView2 bootstrapper, compiles the silent Inno engine, embeds it in the self-contained WPF launcher and writes `dist\GrevHomeSetup.exe` plus its SHA-256 file.

CI also executes the silent engine into a disposable directory and verifies that `GrevHome.exe` was genuinely installed before publishing a release. Runtime failures retain a timestamped engine log under `%LOCALAPPDATA%\Grev Home\Installer Logs`; the custom failure screen reports the useful engine detail and log location. A running Grev Home instance is detected before the engine starts and produces a direct instruction instead of an unexplained exit code.
