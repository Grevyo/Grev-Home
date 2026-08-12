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
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint WsDisabled = 0x08000000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const int MinimumInteractiveWidth = 240;
    private const int MinimumInteractiveHeight = 140;
    private static readonly TimeSpan ProcessIdentityTolerance = TimeSpan.FromSeconds(1);

    public IReadOnlyList<RuntimeWindow> GetTopLevelWindows(IEnumerable<int> processIds) =>
        EnumerateTopLevelWindows(processIds, includeHidden: false)
            .Where(IsInteractiveWindowCandidate)
            .Select(window => new RuntimeWindow(window.Handle, window.ProcessId, window.Title))
            .ToArray();

    public RuntimeWindowState GetWindowState(IEnumerable<int> processIds)
    {
        var windows = EnumerateTopLevelWindows(processIds, includeHidden: true)
            .Where(IsInteractiveWindowCandidate)
            .ToArray();
        if (windows.Length == 0)
        {
            return RuntimeWindowState.HiddenOrUnavailable;
        }

        if (windows.Any(window => window.IsVisible && !window.IsMinimized))
        {
            return RuntimeWindowState.Visible;
        }

        if (windows.Any(window => window.IsVisible && window.IsMinimized))
        {
            return RuntimeWindowState.Minimized;
        }

        return RuntimeWindowState.HiddenOrUnavailable;
    }

    public bool TryActivate(IEnumerable<int> processIds, bool maximize = false)
    {
        // Electron apps such as Discord can expose several hidden top-level utility surfaces.
        // Never restore an arbitrary HWND just because it belongs to the right process. Only
        // consider normal, titled, desktop-sized interactive windows and prefer the largest one.
        var window = EnumerateTopLevelWindows(processIds, includeHidden: true)
            .Where(IsInteractiveWindowCandidate)
            .OrderByDescending(candidate => candidate.Area)
            .ThenByDescending(candidate => candidate.IsVisible)
            .ThenBy(candidate => candidate.IsMinimized)
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
            _ = ShowWindow(window.Handle, SwRestore);
        }

        // Revalidate after ShowWindow. If Windows exposed a transient/utility HWND that no longer
        // qualifies, report failure so Grev Home remains visible rather than hiding behind it.
        var restored = ReadCandidate(window.Handle, window.ProcessId);
        if (restored is null || !restored.IsVisible || !IsInteractiveWindowCandidate(restored))
        {
            return false;
        }

        _ = BringWindowToTop(window.Handle);
        var foregrounded = SetForegroundWindow(window.Handle);

        // SetForegroundWindow can legitimately be denied by Windows focus-stealing rules. A real,
        // validated visible app window is still safe: once Grev Home hides, that window is exposed.
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

    public bool ForceTerminate(
        IEnumerable<RuntimeProcessIdentity> processIdentities,
        bool entireProcessTree = true)
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

                // Launchers such as Steam can own unrelated game descendants. Their package can
                // explicitly request process-only termination so closing the launcher never turns
                // into an implicit force-kill of a running game.
                process.Kill(entireProcessTree);
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

            var candidate = ReadCandidate(handle, (int)processId);
            if (candidate is not null)
            {
                windows.Add(candidate);
            }
            return true;
        }, nint.Zero);

        return windows;
    }

    private static RuntimeWindowCandidate? ReadCandidate(nint handle, int processId)
    {
        if (!IsWindow(handle))
        {
            return null;
        }

        var isMinimized = IsIconic(handle);
        var rect = GetUsableWindowRect(handle, isMinimized);
        if (rect is null)
        {
            return null;
        }

        var width = Math.Max(0, rect.Value.Right - rect.Value.Left);
        var height = Math.Max(0, rect.Value.Bottom - rect.Value.Top);
        var style = unchecked((uint)GetWindowLong(handle, GwlStyle));
        var exStyle = unchecked((uint)GetWindowLong(handle, GwlExStyle));

        return new RuntimeWindowCandidate(
            handle,
            processId,
            GetWindowTitle(handle),
            IsWindowVisible(handle),
            isMinimized,
            width,
            height,
            style,
            exStyle);
    }

    private static RECT? GetUsableWindowRect(nint handle, bool isMinimized)
    {
        if (isMinimized)
        {
            var placement = new WINDOWPLACEMENT
            {
                Length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>()
            };
            if (GetWindowPlacement(handle, ref placement))
            {
                return placement.NormalPosition;
            }
        }

        return GetWindowRect(handle, out var rect) ? rect : null;
    }

    private static bool IsInteractiveWindowCandidate(RuntimeWindowCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Title) ||
            candidate.Width < MinimumInteractiveWidth ||
            candidate.Height < MinimumInteractiveHeight)
        {
            return false;
        }

        if ((candidate.Style & WsDisabled) != 0)
        {
            return false;
        }

        var disallowedExStyles = WsExTransparent | WsExToolWindow | WsExNoActivate;
        return (candidate.ExStyle & disallowedExStyles) == 0;
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
        bool IsMinimized,
        int Width,
        int Height,
        uint Style,
        uint ExStyle)
    {
        public long Area => (long)Width * Height;
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint handle, ref WINDOWPLACEMENT placement);

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
