# Controller system shortcuts

Grev Home system shortcuts are configuration, not hard-coded controller combinations.

The runtime reads:

```text
C:\GrevHome\Data\Input\controller-shortcuts.json
```

`GREV_HOME_ROOT` can redirect the entire Grev Home data root for development/testing. Normal Grev Home operation uses `C:\GrevHome`, which is created automatically on first run.

## Default configuration

On first run Grev Home creates the shortcut file with the current defaults:

```json
{
  "Version": 1,
  "Bindings": [
    {
      "Id": "return-home-default",
      "Action": "ReturnHome",
      "Buttons": [
        "LeftShoulder",
        "RightShoulder",
        "View"
      ],
      "HoldMilliseconds": 700,
      "Enabled": true,
      "TriggerThreshold": 160
    },
    {
      "Id": "overlay-default",
      "Action": "Overlay",
      "Buttons": [
        "LeftShoulder",
        "RightShoulder",
        "Menu"
      ],
      "HoldMilliseconds": 450,
      "Enabled": true,
      "TriggerThreshold": 160
    }
  ]
}
```

These are defaults only. The input engine does not contain a special `LB + RB + View` or `LB + RB + Menu` check.

## Available controller inputs

A binding can use any combination of:

```text
DPadUp
DPadDown
DPadLeft
DPadRight
Menu
View
LeftThumb
RightThumb
LeftShoulder
RightShoulder
A
B
X
Y
LeftTrigger
RightTrigger
```

`TriggerThreshold` controls how far either configured trigger must be pressed before it counts as down. It is ignored when the binding contains no triggers.

A binding may contain between 1 and 8 distinct controller inputs and a hold time from 0 to 5000 milliseconds.

## Different combinations

For example, Return Home could be changed to four buttons:

```json
{
  "Id": "return-home-four-button",
  "Action": "ReturnHome",
  "Buttons": [
    "LeftShoulder",
    "RightShoulder",
    "View",
    "Y"
  ],
  "HoldMilliseconds": 900,
  "Enabled": true,
  "TriggerThreshold": 160
}
```

Or the same action can have several alternative combinations:

```json
{
  "Id": "return-home-triggers",
  "Action": "ReturnHome",
  "Buttons": [
    "LeftTrigger",
    "RightTrigger",
    "View"
  ],
  "HoldMilliseconds": 700,
  "Enabled": true,
  "TriggerThreshold": 180
}
```

Both bindings can exist at the same time. Grev Home treats them as alternative ways to request the same system action.

## Overlapping combinations

Bindings define **required inputs**, so unrelated extra held buttons do not invalidate a combination.

If two configured combinations overlap, Grev Home prefers the most specific active binding — the one containing the largest number of required inputs. This allows, for example, a three-button shortcut and a four-button shortcut to share some buttons without both firing when the four-button chord is held.

The exact same combination cannot be assigned to two different system actions because that would be ambiguous.

## Safety and recovery

`ReturnHome` is Grev Home's controller recovery path. It is fully remappable, but at least one enabled Return Home binding must exist. A configuration with no enabled Return Home binding is rejected for that session and Grev Home runs the safe defaults instead.

If the shortcut JSON is malformed, unsupported, unreadable or otherwise invalid, Grev Home leaves that file untouched for later repair and runs the safe defaults for that session rather than risking a machine with no controller escape route.

## Future Settings UI

The file/service is the source of truth. A later controller-friendly Settings screen should edit this same configuration rather than maintaining a second set of bindings. That UI can support actions such as:

- choose an action;
- press/hold the desired controller combination to capture it;
- add or remove buttons from the combination;
- set hold time;
- add multiple bindings for the same action;
- disable a non-essential binding;
- reset one action or all actions to defaults.

Additional global Grev system actions should be added to the same action/binding model rather than by adding hard-coded controller checks to `ControllerInputService`.
