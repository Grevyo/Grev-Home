# Standalone emulator packages

Grev Home 0.17 adds verified portable packages for the systems that are not
covered by RetroArch or PCSX2. Every package is installed silently into the
current GrevID, is prepared for direct launching, and is kept out of Windows
startup by the Store policy.

| Package | Systems | Shared game folder | Remaining user-owned step |
| --- | --- | --- | --- |
| Dolphin 2606a | GameCube, Wii | `GameCube`, `Wii` | None for normal disc images |
| Azahar 2126.1.1 | Nintendo 3DS | `3DS` | System keys for encrypted content |
| RPCS3 0.0.42-20021 | PlayStation 3 | `PS3` | Install Sony's official PS3 firmware once |
| Cemu 2.6 | Wii U | `Wii U` | Keys for encrypted dumps |
| Xenia Canary aee0871 | Xbox 360 | `Xbox 360` | None for supported decrypted content |
| xemu 0.8.136 | Xbox | `Xbox` | Select user-owned MCPX, flash BIOS, and HDD image once |
| Vita3K continuous-2026-09-17 | PlayStation Vita | `PS Vita` | Install official Vita firmware and game packages in Vita3K |

PlayStation 1 and PSP continue to use RetroArch, so Grev Home does not install
duplicate DuckStation or PPSSPP applications. Vita packages are installed into
Vita3K's managed library rather than launched directly as `.vpk` files; the
Store tile opens Vita3K for that installation step.

## Install and update contract

- Downloads come only from each emulator project's official release endpoint.
- The archive version and SHA-256 are pinned in the Store catalogue.
- Archive entries are validated before extraction, then the replacement is
  swapped into place transactionally.
- Mutable configuration, saves, virtual storage, and installed title data are
  retained during update and repair.
- Shared Games and BIOS roots are recorded during setup; firmware, keys, BIOS
  images, and copyrighted system files are never bundled by Grev Home.
- Each installation belongs to one GrevID. Uninstall removes its emulator
  binaries while preserving that profile's configuration and saves.
- Grev Home removes startup entries for every Store-managed application after
  package operations and at the end of a session.

## Controller and readiness behaviour

Every emulator is playable on a pad straight after its silent install. Defaults
are written by `Store/EmulatorControllerDefaults.cs` (PCSX2 and RetroArch by their
own installers) on install, update and repair, but only while the emulator has
no controller mapping of its own: a missing file, or one still holding keyboard
or null defaults. Anything a person mapped is never replaced.

| Emulator | Controller default | Format source |
| --- | --- | --- |
| RetroArch | Pads auto-configure; L3 + R3 opens the quick menu | `input_menu_toggle_gamepad_combo = 2` ([input_defines.h](https://github.com/libretro/RetroArch/blob/master/input/input_defines.h)) |
| PCSX2 | SDL bindings for pad 1, added next to the keyboard ones | PCSX2's automatic-mapping names ([PadDualshock2.cpp](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/SIO/Pad/PadDualshock2.cpp), [SDLInputSource.cpp](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/Input/SDLInputSource.cpp)) |
| Dolphin | GameCube pad, plus Wii Remote + Nunchuk (pointer on right stick, shake on B) | `GCPadNew.ini`, `WiimoteNew.ini` |
| Azahar | SDL bindings with no GUID, so whichever pad is connected works | `qt-config.ini` `[Controls]` ([settings.h](https://github.com/azahar-emu/azahar/blob/master/src/common/settings.h), [sdl_impl.cpp](https://github.com/azahar-emu/azahar/blob/master/src/input_common/sdl/sdl_impl.cpp)) |
| RPCS3 | Player 1 on the XInput handler; RPCS3 fills its own default mapping | `config/input_configs/global/Default.yml` ([pad_config.h](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Emu/Io/pad_config.h)) |
| Cemu | Wii U GamePad on XInput user 0 with Cemu's default mapping | `controllerProfiles/controller0.xml` ([InputManager.cpp](https://github.com/cemu-project/Cemu/blob/main/src/input/InputManager.cpp), [VPADController.cpp](https://github.com/cemu-project/Cemu/blob/main/src/input/emulated/VPADController.cpp)) |
| Xenia, xemu, Vita3K | Native XInput / `input.auto_bind` / SDL game controller | nothing written |

First-run prompts that would stop a controller-only launch are switched off:
Dolphin's auto-update prompt (`[AutoUpdate] UpdateTrack =`), Cemu's update check
(`check_update`; its Getting Started dialog is already skipped because Grev Home
writes `settings.xml`), and RPCS3's welcome box and update check
(`GuiConfigs/CurrentSettings.ini`). Grev Store owns emulator updates.
`tests/EmulatorSetup` checks all of this.
- RPCS3, Vita3K and xemu expose temporary controller-driven mouse and keyboard
  controls for their unavoidable firmware/system-file screens. The first-launch
  guide has a one-press action that disables this layer afterward, leaving the
  emulator's native controller support untouched.
- Store health reports `Setup required` for missing RPCS3 firmware, Vita3K
  firmware, or an unconfigured xemu system image. Missing binaries or profile
  folders remain a separate `Repair recommended` condition.
