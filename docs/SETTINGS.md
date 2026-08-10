# Grev Home Settings backbone

Milestone 0.6 introduces the first real Settings dashboard inside the permanent Grev Home shell.

## Settings ownership rule

Settings are not one undifferentiated JSON file. Each setting belongs to the identity or machine component that actually owns it.

### GrevID-owned settings

These belong to one persistent local account and travel with that account where appropriate:

```text
Display Name
future dashboard preferences
future accessibility preferences
future per-user audio/UI preferences
future account connections
future emulator/app preferences that Grev Home owns
```

0.6 implements Display Name editing through the existing `profile.json`. Username and GrevID remain immutable.

### Machine-wide settings

These apply before or outside a particular user's application context:

```text
controller system shortcuts
future boot/startup behavior
future power/system policies
future network/Bluetooth behavior
future storage/download destinations
future runtime recovery settings
```

0.6 implements controller system shortcuts through:

```text
%ProgramData%\Grev Home\Data\Input\controller-shortcuts.json
```

The settings UI edits the same `ControllerShortcutService` configuration consumed by the runtime; there is no second copy of shortcut state in the UI.

## Controller shortcut editor

The Settings dashboard can:

- show every enabled Return Home and Overlay binding;
- record a replacement combination by physically holding the desired controller buttons;
- add more than one alternative combination for the same action;
- remove a binding;
- adjust hold time in 100 ms steps;
- reset all system shortcuts to defaults;
- apply saved changes to the running input service immediately.

### Recording flow

```text
Select Record / Re-record
        ↓
release the controller button used to open the recorder
        ↓
Grev Home waits for a neutral controller state
        ↓
hold the desired combination together
        ↓
release it
        ↓
validate configuration
        ↓
save + reload runtime shortcuts
```

This avoids accidentally including the `A` press used to select the Record button.

The recorder captures the largest combination held before release and supports up to eight controller inputs. Recording times out after 15 seconds rather than leaving input permanently trapped in capture mode.

## Recovery rule

Return Home remains the controller recovery action. Users may remap it completely, but Grev Home refuses to save a shortcut configuration with no enabled Return Home binding.

If a manually edited shortcut file is invalid, the runtime uses safe defaults for that session instead of losing the controller escape route.

## Account presentation

For a local Primary User, Settings shows:

```text
Display Name   editable
Username       permanent / read-only
GrevID         permanent / read-only
```

Display Name changes update `profile.json` and the active signed-in session without renaming the GrevID directory or changing Username.

Guest or future account types without a local GrevID can still use machine-wide Settings, but local account editing is disabled until a local account is Primary.

## What 0.6 deliberately does not include

0.6 is the Settings/configuration backbone only. It does not yet implement:

- shutdown/restart/sleep controls;
- Wi-Fi or Bluetooth management;
- storage management;
- controller firmware/pairing;
- account migration or Username changes;
- package/Store settings;
- visual themes or Theme Studio.

Those features should plug into this Settings/dashboard structure in later backbone milestones rather than creating independent fullscreen windows or isolated configuration systems.
