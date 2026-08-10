using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrevHome.Runtime;

public sealed class ProcessTreeService
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);

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

    public IReadOnlyList<int> GetAliveProcessIds(IEnumerable<int> processIds)
    {
        var alive = new List<int>();
        foreach (var processId in processIds.Distinct().Where(id => id > 0))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    alive.Add(processId);
                }
            }
            catch (ArgumentException)
            {
                // Process has already exited.
            }
            catch (InvalidOperationException)
            {
                // Process is no longer queryable.
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
