using Microsoft.Win32;

namespace GrevHome.Store.Installers;

/// <summary>
/// Grev Home launches Store apps only when a user asks for them. Vendor installers may add
/// per-user Windows startup entries, so successful package work and managed app shutdown both
/// re-assert that policy without disabling the vendor's separate update mechanisms.
/// </summary>
public static class WindowsAppStartupPolicy
{
    private static readonly string[] RunKeyPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
    ];

    private static readonly string[] StartupApprovalKeyPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
    ];

    public static void DisableFor(GrevStorePackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var identifiers = BuildIdentifiers(package);
        var removedValueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyPath in RunKeyPaths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key is null) continue;

            foreach (var valueName in key.GetValueNames())
            {
                var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                if (!IsMatchingStartupEntry(valueName, command, identifiers)) continue;
                key.DeleteValue(valueName, throwOnMissingValue: false);
                removedValueNames.Add(valueName);
            }
        }

        foreach (var keyPath in StartupApprovalKeyPaths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key is null) continue;

            foreach (var valueName in key.GetValueNames())
            {
                if (!removedValueNames.Contains(valueName) &&
                    !IsMatchingStartupEntry(valueName, string.Empty, identifiers)) continue;
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }

        RemoveStartupFolderLinks(identifiers);
    }

    internal static bool IsMatchingStartupEntry(
        string valueName,
        string command,
        IReadOnlyCollection<string> identifiers)
    {
        foreach (var identifier in identifiers)
        {
            if (string.Equals(valueName.Trim(), identifier, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(command) &&
                identifier.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                ContainsIdentifier(command, identifier)) return true;
        }

        return false;
    }

    private static bool ContainsIdentifier(string value, string identifier)
    {
        var start = 0;
        while ((start = value.IndexOf(identifier, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeIsIdentifier = start > 0 && char.IsLetterOrDigit(value[start - 1]);
            var end = start + identifier.Length;
            var afterIsIdentifier = end < value.Length && char.IsLetterOrDigit(value[end]);
            if (!beforeIsIdentifier && !afterIsIdentifier) return true;
            start = end;
        }

        return false;
    }

    private static IReadOnlyCollection<string> BuildIdentifiers(GrevStorePackageDefinition package)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            package.PackageId,
            package.App.AppId,
            package.App.Name,
            package.Presentation.DisplayName
        };

        foreach (var processName in package.App.Launch.DeclaredProcessNames)
        {
            identifiers.Add(processName);
            identifiers.Add($"{processName}.exe");
        }

        var executableName = Path.GetFileName(Environment.ExpandEnvironmentVariables(package.App.Launch.Executable));
        if (!string.IsNullOrWhiteSpace(executableName) &&
            !string.Equals(executableName, "Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            identifiers.Add(executableName);
        }

        return identifiers.Where(value => value.Length >= 4).ToArray();
    }

    private static void RemoveStartupFolderLinks(IReadOnlyCollection<string> identifiers)
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startupFolder) || !Directory.Exists(startupFolder)) return;

        foreach (var path in Directory.EnumerateFiles(startupFolder))
        {
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (!identifiers.Any(identifier => string.Equals(name, identifier, StringComparison.OrdinalIgnoreCase))) continue;
            File.Delete(path);
        }
    }
}
