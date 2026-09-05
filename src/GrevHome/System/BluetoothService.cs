using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace GrevHome.Machine;

public sealed record BluetoothDeviceStatus(string Id, string Name, bool IsPaired);
public sealed record BluetoothStatus(bool RadioAvailable, bool IsEnabled, IReadOnlyList<BluetoothDeviceStatus> Devices);

public sealed class BluetoothService
{
    public async Task<BluetoothStatus> GetStatusAsync()
    {
        var radio = await GetBluetoothRadioAsync();
        var devices = await GetDevicesAsync();
        return new BluetoothStatus(
            radio is not null,
            radio?.State == RadioState.On,
            devices);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
        {
            throw new InvalidOperationException("Windows did not grant Grev Home permission to change Bluetooth radio state.");
        }

        var radio = await GetBluetoothRadioAsync()
            ?? throw new InvalidOperationException("Windows did not report a Bluetooth radio.");
        var result = await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off);
        if (result != RadioAccessStatus.Allowed)
        {
            throw new InvalidOperationException("Windows did not allow the Bluetooth radio state to change.");
        }
    }

    public async Task PairAsync(string deviceId)
    {
        var device = await DeviceInformation.CreateFromIdAsync(deviceId)
            ?? throw new InvalidOperationException("That Bluetooth device is no longer available.");
        if (device.Pairing.IsPaired)
        {
            return;
        }

        if (!device.Pairing.CanPair)
        {
            throw new InvalidOperationException("Windows reports that this Bluetooth device cannot be paired from Grev Home.");
        }

        var result = await device.Pairing.PairAsync(DevicePairingProtectionLevel.Default);
        if (result.Status is not (DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired))
        {
            throw new InvalidOperationException($"Bluetooth pairing did not complete: {result.Status}.");
        }
    }

    public async Task UnpairAsync(string deviceId)
    {
        var device = await DeviceInformation.CreateFromIdAsync(deviceId)
            ?? throw new InvalidOperationException("That Bluetooth device is no longer available.");
        if (!device.Pairing.IsPaired)
        {
            return;
        }

        var result = await device.Pairing.UnpairAsync();
        if (result.Status is not (DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired))
        {
            throw new InvalidOperationException($"Bluetooth unpair did not complete: {result.Status}.");
        }
    }

    private static async Task<Radio?> GetBluetoothRadioAsync()
    {
        var radios = await Radio.GetRadiosAsync();
        return radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
    }

    private static async Task<IReadOnlyList<BluetoothDeviceStatus>> GetDevicesAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector());
        return devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Name))
            .Select(device => new BluetoothDeviceStatus(
                device.Id,
                device.Name,
                device.Pairing.IsPaired))
            .OrderByDescending(device => device.IsPaired)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
