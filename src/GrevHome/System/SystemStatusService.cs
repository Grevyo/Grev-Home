using System.IO;
using System.Runtime.InteropServices;

namespace GrevHome.Machine;

public sealed record MachineStatus(
    string MachineName,
    string WindowsDescription,
    string Architecture,
    int LogicalProcessors,
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    TimeSpan Uptime,
    string PowerSource,
    int? BatteryPercent);

public sealed record StorageStatus(
    string Name,
    string Label,
    DriveType DriveType,
    long TotalBytes,
    long FreeBytes,
    string Format);

public sealed class SystemStatusService
{
    public MachineStatus GetMachineStatus()
    {
        var memory = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memory))
        {
            throw new InvalidOperationException("Windows could not report memory status.");
        }

        var hasPowerStatus = GetSystemPowerStatus(out var power);

        return new MachineStatus(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            memory.TotalPhysical,
            memory.AvailablePhysical,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            hasPowerStatus ? FormatPowerSource(power.ACLineStatus) : "Unknown",
            hasPowerStatus && power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : null);
    }

    public IReadOnlyList<StorageStatus> GetStorageStatus()
    {
        var results = new List<StorageStatus>();

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            try
            {
                results.Add(new StorageStatus(
                    drive.Name,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "No label" : drive.VolumeLabel,
                    drive.DriveType,
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    drive.DriveFormat));
            }
            catch (IOException)
            {
                // Removable/network media can disappear while the status view is being built.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore drives Windows refuses to query rather than breaking the entire status surface.
            }
        }

        return results
            .OrderBy(drive => drive.DriveType == DriveType.Fixed ? 0 : 1)
            .ThenBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatPowerSource(byte status) => status switch
    {
        0 => "Battery",
        1 => "AC power",
        _ => "Unknown"
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
