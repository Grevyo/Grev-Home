using Microsoft.Win32;

namespace GrevHome.Machine;

/// <summary>
/// "Start Grev Home when Windows starts": a per-user Run entry pointing at the installed
/// GrevHome.exe. With Windows set to sign in automatically, the PC boots straight into Grev Home
/// and never needs a keyboard. Per-user (HKCU) so no admin prompt is involved; the installer
/// removes the entry on uninstall.
/// </summary>
public sealed class WindowsStartupService
{
    public const string ValueName = "GrevHome";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Task Manager's Startup tab records an "enabled/disabled" byte here; 0x02 means enabled.
    private const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string? _executablePath;

    public WindowsStartupService(string? executablePath = null) =>
        _executablePath = executablePath ?? Environment.ProcessPath;

    /// <summary>False when running under the dotnet host during development, where a Run entry
    /// would start the SDK rather than Grev Home.</summary>
    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_executablePath) &&
        string.Equals(_executablePath.Split('\\', '/').Last(), "GrevHome.exe", StringComparison.OrdinalIgnoreCase);

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\"";

    /// <summary>True when the Run entry points at this Grev Home and Task Manager has not disabled it.</summary>
    public bool IsEnabled()
    {
        if (!IsAvailable) return false;
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var command = run?.GetValue(ValueName) as string;
        if (!string.Equals(command?.Trim(), BuildCommand(_executablePath!), StringComparison.OrdinalIgnoreCase)) return false;
        using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedPath);
        return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || IsApprovedEnabled(state);
    }

    public static bool IsApprovedEnabled(byte[] state) => state.Length == 0 || state[0] % 2 == 0;

    public void SetEnabled(bool enabled)
    {
        if (enabled && !IsAvailable)
            throw new InvalidOperationException("Only an installed copy of Grev Home can start with Windows.");
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        using var approved = Registry.CurrentUser.CreateSubKey(StartupApprovedPath, writable: true);
        if (enabled)
        {
            run.SetValue(ValueName, BuildCommand(_executablePath!), RegistryValueKind.String);
            // Clear a previous "disabled" choice from Task Manager so the switch means what it says.
            approved.SetValue(ValueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            approved.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
