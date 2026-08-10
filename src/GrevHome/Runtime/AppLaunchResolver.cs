using System.Diagnostics;
using System.IO;
using GrevHome.Apps;

namespace GrevHome.Runtime;

public sealed class AppLaunchResolver
{
    public ProcessStartInfo Resolve(InstalledAppEntry entry, string? primaryGrevId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.AvailableToCurrentUser)
        {
            throw new InvalidOperationException(entry.AvailabilityMessage ?? "This app is not available to the current user.");
        }

        var definition = entry.Manifest.Definition;
        var executable = ResolveExecutable(definition, entry.BinaryRoot);
        var workingDirectory = ResolveWorkingDirectory(definition, entry.BinaryRoot, entry.DataRoot);
        var arguments = ExpandTokens(
            definition.Launch.Arguments ?? string.Empty,
            entry.BinaryRoot,
            entry.DataRoot,
            primaryGrevId);

        if (entry.DataRoot is not null)
        {
            Directory.CreateDirectory(entry.DataRoot);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        startInfo.Environment["GREV_HOME_APP_ID"] = definition.AppId;
        startInfo.Environment["GREV_HOME_BINARY_ROOT"] = entry.BinaryRoot;

        if (!string.IsNullOrWhiteSpace(primaryGrevId))
        {
            startInfo.Environment["GREV_HOME_GREV_ID"] = primaryGrevId;
        }

        if (!string.IsNullOrWhiteSpace(entry.DataRoot))
        {
            startInfo.Environment["GREV_HOME_APP_DATA"] = entry.DataRoot;
        }

        return startInfo;
    }

    private static string ResolveExecutable(AppDefinition definition, string binaryRoot)
    {
        var configured = Environment.ExpandEnvironmentVariables(definition.Launch.Executable.Trim());
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException($"{definition.Name} has no executable configured.");
        }

        if (definition.InstallStrategy == InstallStrategy.SystemInstalled)
        {
            if (Path.IsPathRooted(configured) && !File.Exists(configured))
            {
                throw new FileNotFoundException($"The configured executable for {definition.Name} was not found.", configured);
            }

            return configured;
        }

        if (Path.IsPathRooted(configured))
        {
            throw new InvalidOperationException(
                $"{definition.Name} is a Grev Home-managed install and must use an executable path relative to its app root.");
        }

        var root = Path.GetFullPath(binaryRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, configured));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{definition.Name} executable escapes its assigned app directory.");
        }

        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"The executable for {definition.Name} was not found.", resolved);
        }

        return resolved;
    }

    private static string ResolveWorkingDirectory(
        AppDefinition definition,
        string binaryRoot,
        string? dataRoot)
    {
        var configured = definition.Launch.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (definition.InstallStrategy != InstallStrategy.SystemInstalled)
            {
                Directory.CreateDirectory(binaryRoot);
                return binaryRoot;
            }

            return Environment.CurrentDirectory;
        }

        var expanded = ExpandTokens(configured, binaryRoot, dataRoot, null);
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.GetFullPath(Path.Combine(binaryRoot, expanded));
        }

        Directory.CreateDirectory(expanded);
        return expanded;
    }

    private static string ExpandTokens(
        string value,
        string binaryRoot,
        string? dataRoot,
        string? grevId)
    {
        return Environment.ExpandEnvironmentVariables(value)
            .Replace("{BinaryRoot}", binaryRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{DataRoot}", dataRoot ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{GrevId}", grevId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
