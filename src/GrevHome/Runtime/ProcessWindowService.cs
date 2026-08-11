using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevHome.Runtime;

public sealed record RuntimeWindow(nint Handle, int ProcessId, string Title);

public enum RuntimeWindowState
{
    HiddenOrUnavailable,
    Visible,
    Minimized
}

public sealed class ProcessWindowService
{
    private const uint WmClose = 0x0010;
    private const int SwMaximize = 3;
    private const int SwRestore = 9;
    private const uint GwOwner = 4;
    private static readonly TimeSpan ProcessIdentityTolerance = TimeSpan.FromSeconds(1);

    public IReadOnlyList<RuntimeWindow> GetTopLevelWindows(IEnumerable<int> processIds) =>
        EnumerateTopLevelWindows(processIds, includeHidden: false)
            .Select(window => new RuntimeWindow(window.Handle, window.ProcessId, window.Title))
            .ToArray();

    public RuntimeWindowState GetWindowState(IEnumerable<int> processIds)
    {
        var windows = EnumerateTopLevelWindows(processIds, includeHidden: false);
        if (windows.Count == 0)
        {
            return RuntimeWindowState.HiddenOrUnavailable;
        }

        return windows.Any(window => !window.IsMinimized)
            ? RuntimeWindowState.Visible
            : RuntimeWindowState.Minimized;
    }

    public bool TryActivate(IEnumerable<int> processIds, bool maximize = false)
    {
        var window = EnumerateTopLevelWindows(processIds, includeHidden: true)
            .OrderByDescending(candidate => candidate.IsVisible)
            .ThenByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.Title))
            .ThenBy(candidate => candidate.ProcessId)
            .FirstOrDefault();
        if (window is null)
        {
            return false;
        }

        if (maximize)
        {
            _ = ShowWindow(window.Handle, SwMaximize);
        }
        else if (!window.IsVisible || window.IsMinimized)
        {
            // Discord and similar tray apps usually retain a hidden top-level window after
            // the title-bar X is pressed. Restoring that existing window is preferable to
            // starting another app process/session.
            _ = ShowWindow(window.Handle, SwRestore);
        }

        _ = BringWindowToTop(window.Handle);
        var foregrounded = SetForegroundWindow(window.Handle);

        // Windows may deny SetForegroundWindow while still successfully restoring/showing the
        // requested window. Returning true once it is visible lets Grev Home hide itself; the
        // shell's activation retry can then finish foregrounding it without launching a duplicate.
        return foregrounded || IsWindowVisible(window.Handle);
    }

    public bool RequestGracefulClose(IEnumerable<RuntimeProcessIdentity> processIdentities)
    {
        var identities = processIdentities
            .Where(identity => identity.ProcessId > 0)
            .GroupBy(identity => identity.ProcessId)
            .Select(group => group.First())
            .ToDictionary(identity => identity.ProcessId);
        if (identities.Count == 0)
        {
            return false;
        }

        var requested = false;
        var windows = GetTopLevelWindows(identities.Keys);
        foreach (var window in windows)
        {
            if (!identities.TryGetValue(window.ProcessId, out var identity) || !IsSameProcess(identity))
            {
                continue;
            }

            requested |= PostMessage(window.Handle, WmClose, nint.Zero, nint.Zero);
        }

        if (requested)
        {
            return true;
        }

        foreach (var identity in identities.Values)
        {
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (!IsSameProcess(process, identity) || process.HasExited)
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

    public bool ForceTerminate(IEnumerable<RuntimeProcessIdentity> processIdentities)
    {
        var identities = processIdentities
            .Where(identity => identity.ProcessId > 0)
            .GroupBy(identity => identity.ProcessId)
            .Select(group => group.First())
            .OrderByDescending(identity => identity.ProcessId)
            .ToArray();
        var killed = false;

        foreach (var identity in identities)
        {
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (!IsSameProcess(process, identity) || process.HasExited)
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

    // Compatibility overloads for older runtime call sites. New tracked-session code should
    // prefer exact RuntimeProcessIdentity values so an adopted system app can safely predate
    // the Grev Home launch session without losing close/kill protection.
    public bool RequestGracefulClose(IEnumerable<int> processIds, DateTimeOffset sessionStartedAtUtc)
    {
        var identities = ResolveSessionIdentities(processIds, sessionStartedAtUtc);
        return RequestGracefulClose(identities);
    }

    public bool ForceTerminate(IEnumerable<int> processIds, DateTimeOffset sessionStartedAtUtc)
    {
        var identities = ResolveSessionIdentities(processIds, sessionStartedAtUtc);
        return ForceTerminate(identities);
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

    private static IReadOnlyList<RuntimeWindowCandidate> EnumerateTopLevelWindows(
        IEnumerable<int> processIds,
        bool includeHidden)
    {
        var allowed = processIds.Where(id => id > 0).ToHashSet();
        if (allowed.Count == 0)
        {
            return Array.Empty<RuntimeWindowCandidate>();
        }

        var windows = new List<RuntimeWindowCandidate>();
        EnumWindows((handle, parameter) =>
        {
            var visible = IsWindowVisible(handle);
            if ((!includeHidden && !visible) || GetWindow(handle, GwOwner) != nint.Zero)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);
            if (!allowed.Contains((int)processId))
            {
                return true;
            }

            windows.Add(new RuntimeWindowCandidate(
                handle,
                (int)processId,
                GetWindowTitle(handle),
                visible,
                IsIconic(handle)));
            return true;
        }, nint.Zero);

        return windows;
    }

    private static IReadOnlyList<RuntimeProcessIdentity> ResolveSessionIdentities(
        IEnumerable<int> processIds,
        DateTimeOffset sessionStartedAtUtc)
    {
        var identities = new List<RuntimeProcessIdentity>();
        foreach (var processId in processIds.Distinct().Where(id => id > 0))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    continue;
                }

                var startedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                if (startedAtUtc < sessionStartedAtUtc.AddSeconds(-5))
                {
                    continue;
                }

                identities.Add(new RuntimeProcessIdentity(processId, startedAtUtc));
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
                // Inaccessible process is not trusted into the identity list.
            }
        }

        return identities;
    }

    private static bool IsSameProcess(RuntimeProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return IsSameProcess(process, identity);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsSameProcess(Process process, RuntimeProcessIdentity identity)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            var currentStartedAtUtc = process.StartTime.ToUniversalTime();
            return Math.Abs((currentStartedAtUtc - identity.StartedAtUtc.UtcDateTime).TotalMilliseconds) <=
                   ProcessIdentityTolerance.TotalMilliseconds;
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

    private sealed record RuntimeWindowCandidate(
        nint Handle,
        int ProcessId,
        string Title,
        bool IsVisible,
        bool IsMinimized);

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
