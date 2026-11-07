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
    bool CanConnect,
    WifiSecurity Security = WifiSecurity.Unknown,
    WifiCipher Cipher = WifiCipher.Aes)
{
    /// <summary>A network Windows has no saved profile for, which Grev Home can join itself.</summary>
    public bool CanJoin => !IsConnected && !CanConnect && WifiProfileBuilder.IsSupported(Security);

    public bool NeedsPassword => WifiProfileBuilder.NeedsPassword(Security);
}

/// <summary>How a network is secured, from its advertised authentication algorithm.</summary>
public enum WifiSecurity
{
    Unknown,
    Open,
    /// <summary>Wi-Fi Enhanced Open: encrypted, no password.</summary>
    Owe,
    Wep,
    WpaPersonal,
    Wpa2Personal,
    Wpa3Personal,
    /// <summary>802.1X networks (work, school, eduroam) need a username and certificates.</summary>
    Enterprise
}

public enum WifiCipher { None, Tkip, Aes, Wep }

public sealed record WifiJoinResult(bool Connected, string Message);

/// <summary>
/// Builds the WLAN profile XML Windows needs to join a network it has never seen, from the
/// password typed on the controller keyboard. Pure, so it is tested without a Wi-Fi adapter.
/// Open, Enhanced Open and WPA/WPA2/WPA3 Personal are supported; WEP (obsolete and insecure) and
/// Enterprise (802.1X) are left to Windows Settings.
/// </summary>
public static class WifiProfileBuilder
{
    public static bool IsSupported(WifiSecurity security) =>
        security is WifiSecurity.Open or WifiSecurity.Owe or WifiSecurity.WpaPersonal or WifiSecurity.Wpa2Personal or WifiSecurity.Wpa3Personal;

    public static bool NeedsPassword(WifiSecurity security) =>
        security is WifiSecurity.WpaPersonal or WifiSecurity.Wpa2Personal or WifiSecurity.Wpa3Personal;

    /// <summary>Maps DOT11_AUTH_ALGORITHM / DOT11_CIPHER_ALGORITHM values (wlantypes.h).</summary>
    public static (WifiSecurity Security, WifiCipher Cipher) Classify(uint authAlgorithm, uint cipherAlgorithm)
    {
        var cipher = cipherAlgorithm switch
        {
            0 => WifiCipher.None,
            1 or 5 or 0x101 => WifiCipher.Wep,
            2 => WifiCipher.Tkip,
            _ => WifiCipher.Aes
        };
        var security = authAlgorithm switch
        {
            1 => cipher == WifiCipher.Wep ? WifiSecurity.Wep : WifiSecurity.Open,
            2 => WifiSecurity.Wep,
            4 => WifiSecurity.WpaPersonal,
            7 => WifiSecurity.Wpa2Personal,
            9 => WifiSecurity.Wpa3Personal,
            10 => WifiSecurity.Owe,
            3 or 6 or 8 or 11 => WifiSecurity.Enterprise,
            _ => WifiSecurity.Unknown
        };
        return (security, cipher);
    }

    public static string Describe(WifiSecurity security) => security switch
    {
        WifiSecurity.Open => "open",
        WifiSecurity.Owe => "enhanced open",
        WifiSecurity.Wep => "WEP",
        WifiSecurity.WpaPersonal => "WPA",
        WifiSecurity.Wpa2Personal => "WPA2",
        WifiSecurity.Wpa3Personal => "WPA3",
        WifiSecurity.Enterprise => "enterprise",
        _ => "unknown security"
    };

    /// <summary>Null when the password suits the network; otherwise why not. A WPA passphrase is
    /// 8–63 printable ASCII characters, or exactly 64 hex digits.</summary>
    public static string? ValidatePassword(WifiSecurity security, string? password)
    {
        if (!NeedsPassword(security)) return null;
        password ??= string.Empty;
        if (password.Length == 64 && password.All(Uri.IsHexDigit)) return null;
        if (password.Length is < 8 or > 63) return "Wi-Fi passwords are 8 to 63 characters long.";
        if (password.Any(character => character is < ' ' or > '~')) return "Wi-Fi passwords use letters, numbers and standard symbols only.";
        return null;
    }

    public static string BuildProfileXml(string ssid, WifiSecurity security, WifiCipher cipher, string? password)
    {
        if (string.IsNullOrWhiteSpace(ssid)) throw new ArgumentException("A network name is required.", nameof(ssid));
        if (!IsSupported(security)) throw new NotSupportedException($"Grev Home can't join {Describe(security)} networks; use Windows Settings for this one.");
        var error = ValidatePassword(security, password);
        if (error is not null) throw new ArgumentException(error, nameof(password));

        var (authentication, encryption) = security switch
        {
            WifiSecurity.Open => ("open", "none"),
            WifiSecurity.Owe => ("OWE", "AES"),
            WifiSecurity.WpaPersonal => ("WPAPSK", cipher == WifiCipher.Tkip ? "TKIP" : "AES"),
            WifiSecurity.Wpa2Personal => ("WPA2PSK", cipher == WifiCipher.Tkip ? "TKIP" : "AES"),
            _ => ("WPA3SAE", "AES")
        };
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(ssid));
        var name = System.Security.SecurityElement.Escape(ssid);
        var sharedKey = NeedsPassword(security)
            ? $"""
                    <sharedKey>
                        <keyType>{(password!.Length == 64 ? "networkKey" : "passPhrase")}</keyType>
                        <protected>false</protected>
                        <keyMaterial>{System.Security.SecurityElement.Escape(password)}</keyMaterial>
                    </sharedKey>
              """
            : string.Empty;
        return $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                <name>{name}</name>
                <SSIDConfig>
                    <SSID>
                        <hex>{hex}</hex>
                        <name>{name}</name>
                    </SSID>
                </SSIDConfig>
                <connectionType>ESS</connectionType>
                <connectionMode>auto</connectionMode>
                <MSM>
                    <security>
                        <authEncryption>
                            <authentication>{authentication}</authentication>
                            <encryption>{encryption}</encryption>
                            <useOneX>false</useOneX>
                        </authEncryption>
            {sharedKey}
                    </security>
                </MSM>
            </WLANProfile>
            """;
    }
}

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
                var (security, cipher) = WifiProfileBuilder.Classify(network.DefaultAuthAlgorithm, network.DefaultCipherAlgorithm);
                networks.Add(new WifiNetwork(
                    ssid,
                    profile,
                    (int)Math.Clamp(network.SignalQuality, 0u, 100u),
                    network.SecurityEnabled,
                    (network.Flags & AvailableNetworkConnectedFlag) != 0,
                    network.NetworkConnectable && !string.IsNullOrWhiteSpace(profile),
                    security,
                    cipher));
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

    /// <summary>
    /// Joins a network Windows has no profile for: saves a profile built from the password,
    /// connects, and waits for Windows to confirm. If the connection does not come up (usually a
    /// wrong password) the new profile is removed again, so a mistake never leaves a broken saved
    /// network behind.
    /// </summary>
    public async Task<WifiJoinResult> JoinAsync(WifiNetwork network, string? password, CancellationToken cancellationToken = default)
    {
        if (!WifiProfileBuilder.IsSupported(network.Security))
        {
            return new WifiJoinResult(false, network.Security == WifiSecurity.Enterprise
                ? $"{network.Ssid} is a work or school (enterprise) network. Join it once from Windows Settings; Grev Home will then connect to it."
                : $"{network.Ssid} uses {WifiProfileBuilder.Describe(network.Security)} security, which Grev Home can't set up. Use Windows Settings for this network.");
        }
        var passwordError = WifiProfileBuilder.ValidatePassword(network.Security, password);
        if (passwordError is not null) return new WifiJoinResult(false, passwordError);

        var xml = WifiProfileBuilder.BuildProfileXml(network.Ssid, network.Security, network.Cipher, password);
        await Task.Run(() => SetProfile(xml), cancellationToken);
        await Task.Run(() => ConnectSavedProfile(network.Ssid), cancellationToken);

        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000, cancellationToken);
            var status = await Task.Run(GetStatus, cancellationToken);
            if (status.IsConnected && string.Equals(status.Ssid, network.Ssid, StringComparison.Ordinal))
                return new WifiJoinResult(true, $"Connected to {network.Ssid}.");
        }

        await Task.Run(() => DeleteProfile(network.Ssid), CancellationToken.None);
        return new WifiJoinResult(false, network.NeedsPassword
            ? $"Couldn't connect to {network.Ssid}. Check the password and try again."
            : $"Couldn't connect to {network.Ssid}. Move closer to the router and try again.");
    }

    private static void SetProfile(string profileXml)
    {
        using var client = OpenClient();
        var selected = SelectInterface(client.Handle);
        var interfaceGuid = selected.InterfaceGuid;
        var error = WlanSetProfile(client.Handle, ref interfaceGuid, 0, profileXml, null, true, IntPtr.Zero, out var reason);
        if (error != 0)
        {
            throw new InvalidOperationException(reason != 0
                ? $"Windows refused the Wi-Fi settings (reason {reason})."
                : new Win32Exception((int)error).Message);
        }
    }

    private static void DeleteProfile(string profileName)
    {
        try
        {
            using var client = OpenClient();
            var selected = SelectInterface(client.Handle);
            var interfaceGuid = selected.InterfaceGuid;
            WlanDeleteProfile(client.Handle, ref interfaceGuid, profileName, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Best effort: a stale profile only means Windows retries a wrong password later.
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

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        uint flags,
        string profileXml,
        string? allUserProfileSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool overwrite,
        IntPtr reserved,
        out uint reasonCode);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanDeleteProfile(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        string profileName,
        IntPtr reserved);
}
