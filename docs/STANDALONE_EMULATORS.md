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

- Dolphin receives a per-GrevID Player 1 XInput mapping during silent setup.
- Azahar, Cemu, Xenia, xemu and Vita3K retain their native SDL/XInput device
  discovery so the active controller is not pinned to another user's device.
- RPCS3, Vita3K and xemu expose temporary controller-driven mouse and keyboard
  controls for their unavoidable firmware/system-file screens. The first-launch
  guide has a one-press action that disables this layer afterward, leaving the
  emulator's native controller support untouched.
- Store health reports `Setup required` for missing RPCS3 firmware, Vita3K
  firmware, or an unconfigured xemu system image. Missing binaries or profile
  folders remain a separate `Repair recommended` condition.
