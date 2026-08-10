using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevHome.Runtime;

public sealed record RuntimeWindow(nint Handle, int ProcessId, string Title);

public sealed class ProcessWindowService
{
    private const uint WmClose = 0x0010;
    private const int SwRestore = 9;
    private const uint GwOwner = 4;

    public IReadOnlyList<RuntimeWindow> GetTopLevelWindows(IEnumerable<int> processIds)
    {
        var allowed = processIds.Where(id => id > 0).ToHashSet();
        if (allowed.Count == 0)
        {
            return Array.Empty<RuntimeWindow>();
        }

        var windows = new List<RuntimeWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, GwOwner) != nint.Zero)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);
            if (!allowed.Contains((int)processId))
            {
                return true;
            }

            windows.Add(new RuntimeWindow(handle, (int)processId, GetWindowTitle(handle)));
            return true;
        }, nint.Zero);

        return windows
            .OrderByDescending(window => !string.IsNullOrWhiteSpace(window.Title))
            .ThenBy(window => window.ProcessId)
            .ToArray();
    }

    public bool TryActivate(IEnumerable<int> processIds)
    {
        var window = GetTopLevelWindows(processIds).FirstOrDefault();
        if (window is null)
        {
            return false;
        }

        if (IsIconic(window.Handle))
        {
            _ = ShowWindow(window.Handle, SwRestore);
        }

        _ = BringWindowToTop(window.Handle);
        return SetForegroundWindow(window.Handle);
    }

    public bool RequestGracefulClose(IEnumerable<int> processIds, DateTimeOffset sessionStartedAtUtc)
    {
        var requested = false;
        var windows = GetTopLevelWindows(processIds);

        foreach (var window in windows)
        {
            if (!IsProcessFromSession(window.ProcessId, sessionStartedAtUtc))
            {
                continue;
            }

            requested |= PostMessage(window.Handle, WmClose, nint.Zero, nint.Zero);
        }

        if (requested)
        {
            return true;
        }

        foreach (var processId in processIds.Distinct().Where(id => id > 0))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!IsProcessFromSession(process, sessionStartedAtUtc) || process.HasExited)
                {
                    continue;
                }

                requested |= process.CloseMainWindow();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
            catch (InvalidOperationException)
            {
                // Process is no longer queryable.
            }
        }

        return requested;
    }

    public bool ForceTerminate(IEnumerable<int> processIds, DateTimeOffset sessionStartedAtUtc)
    {
        var killed = false;

        foreach (var processId in processIds.Distinct().Where(id => id > 0).OrderByDescending(id => id))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!IsProcessFromSession(process, sessionStartedAtUtc) || process.HasExited)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                killed = true;
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
            catch (InvalidOperationException)
            {
                // Process is no longer queryable.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied / protected process. The caller can report failure without crashing Grev Home.
            }
        }

        return killed;
    }

    public int? GetForegroundProcessId()
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        return processId == 0 ? null : (int)processId;
    }

    private static bool IsProcessFromSession(int processId, DateTimeOffset sessionStartedAtUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return IsProcessFromSession(process, sessionStartedAtUtc);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsProcessFromSession(Process process, DateTimeOffset sessionStartedAtUtc)
    {
        try
        {
            var startedAtUtc = process.StartTime.ToUniversalTime();
            return startedAtUtc >= sessionStartedAtUtc.UtcDateTime.AddSeconds(-5);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
