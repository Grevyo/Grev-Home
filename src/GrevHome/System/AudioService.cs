using System.Runtime.InteropServices;

namespace GrevHome.Machine;

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);
public sealed record AudioStatus(string OutputDeviceId, string OutputDeviceName, int VolumePercent, bool IsMuted);

public sealed class AudioService
{
    private const uint DeviceStateActive = 0x00000001;
    private const int StgmRead = 0;
    private const uint ClsctxAll = 23;
    private static readonly Guid AudioEndpointVolumeGuid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly PropertyKey DeviceFriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public AudioStatus GetStatus()
    {
        var enumerator = CreateEnumerator();
        IMMDevice? device = null;
        IAudioEndpointVolume? endpointVolume = null;
        try
        {
            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
            var id = GetDeviceId(device);
            var name = GetFriendlyName(device) ?? "Default Windows output";
            endpointVolume = ActivateEndpointVolume(device);
            ThrowIfFailed(endpointVolume.GetMasterVolumeLevelScalar(out var volume));
            ThrowIfFailed(endpointVolume.GetMute(out var muted));
            return new AudioStatus(id, name, Math.Clamp((int)Math.Round(volume * 100f), 0, 100), muted);
        }
        finally
        {
            ReleaseCom(endpointVolume);
            ReleaseCom(device);
            ReleaseCom(enumerator);
        }
    }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var enumerator = CreateEnumerator();
        IMMDevice? defaultDevice = null;
        IMMDeviceCollection? collection = null;
        try
        {
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out defaultDevice) >= 0)
            {
                defaultId = GetDeviceId(defaultDevice);
            }

            ThrowIfFailed(enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out collection));
            ThrowIfFailed(collection.GetCount(out var count));
            var results = new List<AudioOutputDevice>((int)count);

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device));
                    var id = GetDeviceId(device);
                    results.Add(new AudioOutputDevice(
                        id,
                        GetFriendlyName(device) ?? $"Audio output {index + 1}",
                        string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    ReleaseCom(device);
                }
            }

            return results
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            ReleaseCom(collection);
            ReleaseCom(defaultDevice);
            ReleaseCom(enumerator);
        }
    }

    public AudioStatus SetVolume(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        WithEndpointVolume(volume =>
        {
            var context = Guid.Empty;
            ThrowIfFailed(volume.SetMasterVolumeLevelScalar(clamped / 100f, ref context));
        });
        return GetStatus();
    }

    public AudioStatus SetMuted(bool muted)
    {
        WithEndpointVolume(volume =>
        {
            var context = Guid.Empty;
            ThrowIfFailed(volume.SetMute(muted, ref context));
        });
        return GetStatus();
    }

    public AudioStatus SetDefaultOutput(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("An audio output device is required.", nameof(deviceId));
        }

        IPolicyConfigVista? policy = null;
        try
        {
            policy = (IPolicyConfigVista)(object)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                ThrowIfFailed(policy.SetDefaultEndpoint(deviceId, role));
            }
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                "Windows did not allow Grev Home to change the default audio output on this build.",
                ex);
        }
        finally
        {
            ReleaseCom(policy);
        }

        return GetStatus();
    }

    private static void WithEndpointVolume(Action<IAudioEndpointVolume> action)
    {
        var enumerator = CreateEnumerator();
        IMMDevice? device = null;
        IAudioEndpointVolume? endpointVolume = null;
        try
        {
            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
            endpointVolume = ActivateEndpointVolume(device);
            action(endpointVolume);
        }
        finally
        {
            ReleaseCom(endpointVolume);
            ReleaseCom(device);
            ReleaseCom(enumerator);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator() =>
        (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();

    private static IAudioEndpointVolume ActivateEndpointVolume(IMMDevice device)
    {
        var iid = AudioEndpointVolumeGuid;
        ThrowIfFailed(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated));
        return (IAudioEndpointVolume)activated;
    }

    private static string GetDeviceId(IMMDevice device)
    {
        ThrowIfFailed(device.GetId(out var id));
        return id;
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        PropVariant value = default;
        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out propertyStore));
            var key = DeviceFriendlyNameKey;
            ThrowIfFailed(propertyStore.GetValue(ref key, out value));
            return value.GetString();
        }
        finally
        {
            PropVariantClear(ref value);
            ReleaseCom(propertyStore);
        }
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _variantType;
        [FieldOffset(8)] private IntPtr _pointerValue;

        public readonly string? GetString() =>
            _variantType == 31 ? Marshal.PtrToStringUni(_pointerValue) : null;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A6F2BCF5B9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsctx, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        [PreserveSig] int OpenPropertyStore(int accessMode, out IPropertyStore propertyStore);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channelNumber, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channelNumber, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float volumeMinDb, out float volumeMaxDb, out float volumeIncrementDb);
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("568B9108-44BF-40B4-9006-86AFE5B5A620")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, IntPtr defaultPeriodValue, IntPtr minimumPeriodValue);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);
}
