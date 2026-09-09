using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrevHome.Runtime;

public sealed class ProcessTreeService
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);
    private static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(1);

    public IReadOnlySet<int> DiscoverDescendants(IEnumerable<int> knownProcessIds)
    {
        var known = new HashSet<int>(knownProcessIds.Where(id => id > 0));
        if (known.Count == 0)
        {
            return known;
        }

        var entries = ReadProcessEntries();
        var changed = true;

        while (changed)
        {
            changed = false;
            foreach (var entry in entries)
            {
                if (known.Contains(entry.ProcessId) || !known.Contains(entry.ParentProcessId))
                {
                    continue;
                }

                known.Add(entry.ProcessId);
                changed = true;
            }
        }

        return known;
    }

    public IReadOnlyList<RuntimeProcessIdentity> GetProcessIdentitiesByName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Array.Empty<RuntimeProcessIdentity>();
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (normalized.Length == 0 || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            return Array.Empty<RuntimeProcessIdentity>();
        }

        var identities = new List<RuntimeProcessIdentity>();
        foreach (var process in Process.GetProcessesByName(normalized))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        identities.Add(new RuntimeProcessIdentity(
                            process.Id,
                            process.StartTime.ToUniversalTime()));
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while Windows was enumerating it.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // An inaccessible process with the same name is not trusted into the session.
                }
            }
        }

        return identities
            .GroupBy(identity => identity.ProcessId)
            .Select(group => group.First())
            .OrderBy(identity => identity.ProcessId)
            .ToArray();
    }

    public RuntimeProcessIdentity? TryGetProcessIdentity(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return null;
            }

            return new RuntimeProcessIdentity(processId, process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public bool IsSameLiveProcess(RuntimeProcessIdentity identity)
    {
        var current = TryGetProcessIdentity(identity.ProcessId);
        if (current is null)
        {
            return false;
        }

        return Math.Abs((current.StartedAtUtc - identity.StartedAtUtc).TotalMilliseconds) <=
               ProcessStartTolerance.TotalMilliseconds;
    }

    public IReadOnlyList<RuntimeProcessIdentity> GetAliveProcessIdentities(
        IEnumerable<RuntimeProcessIdentity> identities)
    {
        return identities
            .GroupBy(identity => identity.ProcessId)
            .Select(group => group.First())
            .Where(IsSameLiveProcess)
            .OrderBy(identity => identity.ProcessId)
            .ToArray();
    }

    public IReadOnlyList<int> GetAliveProcessIds(IEnumerable<int> processIds)
    {
        var alive = new List<int>();
        foreach (var processId in processIds.Distinct().Where(id => id > 0))
        {
            if (TryGetProcessIdentity(processId) is not null)
            {
                alive.Add(processId);
            }
        }

        return alive;
    }

    private static IReadOnlyList<ProcessEntry> ReadProcessEntries()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == nint.Zero || snapshot == InvalidHandleValue)
        {
            return Array.Empty<ProcessEntry>();
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };

            if (!Process32First(snapshot, ref entry))
            {
                return Array.Empty<ProcessEntry>();
            }

            var results = new List<ProcessEntry>();
            do
            {
                results.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId));
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            return results;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private sealed record ProcessEntry(int ProcessId, int ParentProcessId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
