using System.Runtime.InteropServices;

namespace GrevHome.System;

public sealed record ControllerHardwareStatus(
    int ControllerIndex,
    bool IsConnected,
    string BatteryType,
    string BatteryLevel);

public sealed class ControllerHardwareService
{
    private const byte BatteryDeviceTypeGamepad = 0x00;

    public IReadOnlyList<ControllerHardwareStatus> GetControllers()
    {
        var results = new List<ControllerHardwareStatus>(4);

        for (var index = 0; index < 4; index++)
        {
            var connected = XInputGetState((uint)index, out _) == 0;
            if (!connected)
            {
                results.Add(new ControllerHardwareStatus(index, false, "Unknown", "Not connected"));
                continue;
            }

            var batteryType = "Unknown";
            var batteryLevel = "Unknown";
            if (XInputGetBatteryInformation((uint)index, BatteryDeviceTypeGamepad, out var battery) == 0)
            {
                batteryType = FormatBatteryType(battery.BatteryType);
                batteryLevel = FormatBatteryLevel(battery.BatteryLevel, battery.BatteryType);
            }

            results.Add(new ControllerHardwareStatus(index, true, batteryType, batteryLevel));
        }

        return results;
    }

    private static string FormatBatteryType(byte type) => type switch
    {
        0x00 => "Disconnected",
        0x01 => "Wired",
        0x02 => "Alkaline",
        0x03 => "NiMH",
        _ => "Unknown"
    };

    private static string FormatBatteryLevel(byte level, byte type)
    {
        if (type == 0x01)
        {
            return "Wired";
        }

        return level switch
        {
            0x00 => "Empty",
            0x01 => "Low",
            0x02 => "Medium",
            0x03 => "Full",
            _ => "Unknown"
        };
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInputGetBatteryInformation(
        uint userIndex,
        byte devType,
        out XInputBatteryInformation batteryInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }
}
