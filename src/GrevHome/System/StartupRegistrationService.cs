using System.IO;
using Microsoft.Win32;

namespace GrevHome.Machine;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Grev Home";

    public bool IsApplianceExecutable
    {
        get
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) ||
                !string.Equals(Path.GetFileName(executable), "GrevHome.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(executable);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (fullPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // dotnet run / IDE builds must never register themselves as the machine's console shell.
            var normalized = fullPath.Replace('/', '\\');
            return !normalized.Contains("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.Contains("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool TryEnsureRegistered(out string? failure)
    {
        failure = null;
        if (!IsApplianceExecutable)
        {
            return false;
        }

        var executable = Environment.ProcessPath!;
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                failure = "Windows did not expose the current-user startup registry key.";
                return false;
            }

            var expected = $"\"{executable}\"";
            var current = runKey.GetValue(ValueName) as string;
            if (!string.Equals(current, expected, StringComparison.Ordinal))
            {
                runKey.SetValue(ValueName, expected, RegistryValueKind.String);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            failure = ex.Message;
            return false;
        }
    }
}
