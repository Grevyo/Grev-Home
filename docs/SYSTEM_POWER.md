# Grev Home system and power backbone

Milestone 0.7 extends the 0.6 Settings framework with machine status, storage/controller hardware visibility and controller-safe Windows power actions.

## System status

The Settings & System surface reads live machine information when the view opens and when the user selects **Refresh status**.

It currently shows:

- Windows description and OS architecture;
- logical processor count;
- total and available physical memory;
- Windows uptime;
- AC/battery power source where Windows reports it;
- system battery percentage where available;
- every ready Windows drive, including drive type, filesystem, total size, free space and used percentage;
- XInput Controller 1-4 connection state;
- XInput battery type/level where supported.

The existing Users & Controllers lobby remains the source of truth for Grev Home session-user/controller assignments. The system-status view reports hardware state rather than duplicating session ownership rules.

## Power actions

The first supported machine-wide power actions are:

```text
Sleep
Restart
Shut Down
```

A single controller press never performs one of these actions.

Power uses a two-stage confirmation contract:

1. select the desired power action;
2. Grev Home arms that exact action for 8 seconds;
3. select the same action again within that window to execute it;
4. selecting a different action arms the new action instead;
5. Cancel clears any armed action.

This is intentionally similar to the deliberate Force Close pattern used by App Killer.

## Service boundary

Power buttons do not implement Windows commands directly. `SystemPowerService` owns the operating-system request.

- Shutdown/Restart dispatch through the Windows system `shutdown.exe` tool.
- Sleep uses the Windows power API.

`SystemStatusService` separately owns machine/storage status, and `ControllerHardwareService` owns XInput hardware/battery inspection.

This keeps UI code responsible for presentation and confirmation while OS-specific behavior stays behind services that later surfaces, such as the Grev Overlay, can reuse.

## Current boundary

0.7 does not yet add:

- Wi-Fi configuration;
- Bluetooth pairing;
- audio-device/volume management;
- display/resolution management;
- Windows Update controls;
- disk formatting/partition management;
- Hibernate;
- advanced power plans;
- running-app-aware shutdown blocking;
- UPS management.

Those should be added only when their controller-first flows and safety behavior are designed.

## Manual smoke test

1. Open Dashboard → Settings & System using only a controller.
2. Verify Windows, CPU/logical-processor, RAM and uptime information appear.
3. Verify fixed/removable ready drives appear with capacity/free-space information.
4. Connect/disconnect XInput controllers and use Refresh status to verify their hardware state changes.
5. If using a battery-powered XInput controller, verify Windows' battery level category is displayed.
6. Select Sleep once and confirm the button changes to `CONFIRM SLEEP` without sleeping the machine.
7. Select Cancel and confirm the action disarms.
8. Arm Restart, wait more than 8 seconds, then select Restart again and confirm it re-arms rather than immediately restarting.
9. On a disposable/manual-test machine, confirm Sleep resumes successfully.
10. Only when safe, verify the two-step Restart and Shut Down flows perform the requested Windows action.
