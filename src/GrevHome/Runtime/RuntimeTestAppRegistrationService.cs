using System.IO;
using System.Text.Json;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Runtime;

internal static class RuntimeTestAppRegistrationService
{
    public const string EnvironmentVariable = "GREV_HOME_RUNTIME_TEST";
    public const string TestAppId = "grev-runtime-test";
    private const string ManifestName = "installed.grevapp.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            "1",
            StringComparison.OrdinalIgnoreCase);

    public static void ConfigureForCurrentRun(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureMachineLayout();

        var appRoot = paths.GetGlobalAppRoot(TestAppId);
        var manifestPath = Path.Combine(appRoot, ManifestName);

        if (!IsEnabled)
        {
            RemoveTestRegistration(appRoot, manifestPath);
            return;
        }

        var executable = ResolveSafeWindowsTestExecutable();
        Directory.CreateDirectory(appRoot);

        var definition = new AppDefinition(
            TestAppId,
            "Grev Runtime Test",
            AppKind.Utility,
            InstallStrategy.SystemInstalled,
            DataStrategy.NativeAccount,
            new AppLaunchDefinition(
                executable,
                WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.System)),
            SupportsController: false,
            Description: "Development-only runtime recovery test app. Uses a built-in Windows utility and is removed from the Installed Library when the runtime-test flag is not enabled.");

        var manifest = new InstalledAppManifest(
            definition,
            "0.9-test",
            DateTimeOffset.UtcNow,
            OwnerGrevId: null);

        var temporaryPath = manifestPath + ".tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, manifest, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, manifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ResolveSafeWindowsTestExecutable()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidates = new[]
        {
            Path.Combine(systemDirectory, "charmap.exe"),
            Path.Combine(systemDirectory, "notepad.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "Grev Home could not find Character Map or Notepad for the development runtime test.");
    }

    private static void RemoveTestRegistration(string appRoot, string manifestPath)
    {
        try
        {
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            if (Directory.Exists(appRoot) && !Directory.EnumerateFileSystemEntries(appRoot).Any())
            {
                Directory.Delete(appRoot);
            }
        }
        catch (IOException)
        {
            // A stale development test registration must never prevent Grev Home from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // Leave the stale test manifest untouched rather than failing the shell.
        }
    }
}
