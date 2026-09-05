using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevHome.Machine;

public sealed record WifiStatus(
    bool AdapterAvailable,
    string AdapterName,
    bool IsConnected,
    string? Ssid,
    string? ProfileName,
    int SignalQuality);

public sealed record WifiNetwork(
    string Ssid,
    string? ProfileName,
    int SignalQuality,
    bool IsSecure,
    bool IsConnected,
    bool CanConnect);

public sealed class WifiService
{
    private const uint ClientVersion = 2;
    private const uint AvailableNetworkIncludeAllAdhocProfiles = 0x00000001;
    private const uint AvailableNetworkIncludeAllManualHiddenProfiles = 0x00000002;
    private const uint AvailableNetworkConnectedFlag = 0x00000001;

    public WifiStatus GetStatus()
    {
        using var client = OpenClient();
        var interfaces = EnumerateInterfaces(client.Handle);
        if (interfaces.Count == 0)
        {
            return new WifiStatus(false, "No Wi-Fi adapter", false, null, null, 0);
        }

        var selected = interfaces.FirstOrDefault(item => item.State == WlanInterfaceState.Connected)
                       ?? interfaces[0];
        if (selected.State != WlanInterfaceState.Connected)
        {
            return new WifiStatus(true, selected.Description, false, null, null, 0);
        }

        var connection = QueryCurrentConnection(client.Handle, selected.InterfaceGuid);
        return new WifiStatus(
            true,
            selected.Description,
            connection.State == WlanInterfaceState.Connected,
            ReadSsid(connection.Association.Dot11Ssid),
            NullIfEmpty(connection.ProfileName),
            (int)Math.Clamp(connection.Association.SignalQuality, 0u, 100u));
    }

    public IReadOnlyList<WifiNetwork> GetAvailableNetworks()
    {
        using var client = OpenClient();
        var interfaces = EnumerateInterfaces(client.Handle);
        if (interfaces.Count == 0)
        {
            return Array.Empty<WifiNetwork>();
        }

        var selected = interfaces.FirstOrDefault(item => item.State == WlanInterfaceState.Connected)
                       ?? interfaces[0];
        var interfaceGuid = selected.InterfaceGuid;
        var flags = AvailableNetworkIncludeAllAdhocProfiles | AvailableNetworkIncludeAllManualHiddenProfiles;
        ThrowIfError(WlanGetAvailableNetworkList(
            client.Handle,
            ref interfaceGuid,
            flags,
            IntPtr.Zero,
            out var listPointer));

        try
        {
            var count = Marshal.ReadInt32(listPointer, 0);
            var itemSize = Marshal.SizeOf<WlanAvailableNetwork>();
            var networks = new List<WifiNetwork>(count);
            for (var index = 0; index < count; index++)
            {
                var itemPointer = IntPtr.Add(listPointer, 8 + index * itemSize);
                var network = Marshal.PtrToStructure<WlanAvailableNetwork>(itemPointer);
                var ssid = ReadSsid(network.Dot11Ssid);
                if (string.IsNullOrWhiteSpace(ssid))
                {
                    continue;
                }

                var profile = NullIfEmpty(network.ProfileName);
                networks.Add(new WifiNetwork(
                    ssid,
                    profile,
                    (int)Math.Clamp(network.SignalQuality, 0u, 100u),
                    network.SecurityEnabled,
                    (network.Flags & AvailableNetworkConnectedFlag) != 0,
                    network.NetworkConnectable && !string.IsNullOrWhiteSpace(profile)));
            }

            return networks
                .GroupBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(network => network.IsConnected)
                    .ThenByDescending(network => network.SignalQuality)
                    .First())
                .OrderByDescending(network => network.IsConnected)
                .ThenByDescending(network => network.SignalQuality)
                .ThenBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    public void ConnectSavedProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("A saved Wi-Fi profile is required.", nameof(profileName));
        }

        using var client = OpenClient();
        var selected = SelectInterface(client.Handle);
        var interfaceGuid = selected.InterfaceGuid;
        var profilePointer = Marshal.StringToHGlobalUni(profileName.Trim());
        try
        {
            var parameters = new WlanConnectionParameters
            {
                ConnectionMode = WlanConnectionMode.Profile,
                Profile = profilePointer,
                Dot11Ssid = IntPtr.Zero,
                DesiredBssidList = IntPtr.Zero,
                Dot11BssType = Dot11BssType.Any,
                Flags = 0
            };
            ThrowIfError(WlanConnect(client.Handle, ref interfaceGuid, ref parameters, IntPtr.Zero));
        }
        finally
        {
            Marshal.FreeHGlobal(profilePointer);
        }
    }

    public void Disconnect()
    {
        using var client = OpenClient();
        var selected = SelectInterface(client.Handle);
        var interfaceGuid = selected.InterfaceGuid;
        ThrowIfError(WlanDisconnect(client.Handle, ref interfaceGuid, IntPtr.Zero));
    }

    private static WifiInterface SelectInterface(IntPtr clientHandle)
    {
        var interfaces = EnumerateInterfaces(clientHandle);
        if (interfaces.Count == 0)
        {
            throw new InvalidOperationException("Windows did not report a Wi-Fi adapter.");
        }

        return interfaces.FirstOrDefault(item => item.State == WlanInterfaceState.Connected)
               ?? interfaces[0];
    }

    private static IReadOnlyList<WifiInterface> EnumerateInterfaces(IntPtr clientHandle)
    {
        ThrowIfError(WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var listPointer));
        try
        {
            var count = Marshal.ReadInt32(listPointer, 0);
            var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
            var results = new List<WifiInterface>(count);
            for (var index = 0; index < count; index++)
            {
                var itemPointer = IntPtr.Add(listPointer, 8 + index * itemSize);
                var info = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer);
                results.Add(new WifiInterface(info.InterfaceGuid, info.Description, info.State));
            }

            return results;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static WlanConnectionAttributes QueryCurrentConnection(IntPtr clientHandle, Guid interfaceGuid)
    {
        var guid = interfaceGuid;
        ThrowIfError(WlanQueryInterface(
            clientHandle,
            ref guid,
            WlanIntfOpcode.CurrentConnection,
            IntPtr.Zero,
            out _,
            out var dataPointer,
            out _));
        try
        {
            return Marshal.PtrToStructure<WlanConnectionAttributes>(dataPointer);
        }
        finally
        {
            WlanFreeMemory(dataPointer);
        }
    }

    private static WlanClientHandle OpenClient()
    {
        ThrowIfError(WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var handle));
        return new WlanClientHandle(handle);
    }

    private static string ReadSsid(Dot11Ssid ssid)
    {
        var bytes = ssid.Ssid ?? Array.Empty<byte>();
        var length = (int)Math.Min(ssid.SsidLength, (uint)bytes.Length);
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ThrowIfError(uint error)
    {
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }
    }

    private sealed class WlanClientHandle : IDisposable
    {
        public IntPtr Handle { get; }

        public WlanClientHandle(IntPtr handle) => Handle = handle;

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                WlanCloseHandle(Handle, IntPtr.Zero);
            }
        }
    }

    private sealed record WifiInterface(Guid InterfaceGuid, string Description, WlanInterfaceState State);

    private enum WlanInterfaceState
    {
        NotReady,
        Connected,
        AdHocNetworkFormed,
        Disconnecting,
        Disconnected,
        Associating,
        Discovering,
        Authenticating
    }

    private enum WlanConnectionMode
    {
        Profile,
        TemporaryProfile,
        DiscoverySecure,
        DiscoveryUnsecure,
        Auto,
        Invalid
    }

    private enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    private enum WlanIntfOpcode
    {
        AutoconfEnabled = 1,
        BackgroundScanEnabled,
        MediaStreamingMode,
        RadioState,
        BssType,
        InterfaceState,
        CurrentConnection
    }

    private enum WlanOpcodeValueType
    {
        QueryOnly,
        SetByGroupPolicy,
        SetByUser,
        Invalid
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public WlanInterfaceState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint SsidLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Ssid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Dot11Ssid Dot11Ssid;
        public Dot11BssType Dot11BssType;
        public uint NumberOfBssids;
        [MarshalAs(UnmanagedType.Bool)] public bool NetworkConnectable;
        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] Dot11PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint SignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint DefaultAuthAlgorithm;
        public uint DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public WlanInterfaceState State;
        public WlanConnectionMode ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public WlanAssociationAttributes Association;
        public WlanSecurityAttributes Security;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Dot11Ssid;
        public Dot11BssType Dot11BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Dot11Bssid;
        public uint Dot11PhyType;
        public uint Dot11PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        [MarshalAs(UnmanagedType.Bool)] public bool OneXEnabled;
        public uint AuthAlgorithm;
        public uint CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanConnectionParameters
    {
        public WlanConnectionMode ConnectionMode;
        public IntPtr Profile;
        public IntPtr Dot11Ssid;
        public IntPtr DesiredBssidList;
        public Dot11BssType Dot11BssType;
        public uint Flags;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        uint flags,
        IntPtr reserved,
        out IntPtr availableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        WlanIntfOpcode opcode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out WlanOpcodeValueType opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanConnect(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        ref WlanConnectionParameters connectionParameters,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanDisconnect(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
