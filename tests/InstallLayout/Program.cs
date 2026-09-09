using GrevHome.Storage;

var previousOverride = Environment.GetEnvironmentVariable("GREV_HOME_ROOT");
try
{
    Environment.SetEnvironmentVariable("GREV_HOME_ROOT", null);

    var paths = new AppPaths();
    Check(PathsEqual(paths.Root, @"C:\GrevCo\GrevHome"),
        $"Default Grev Home root must be C:\\GrevCo\\GrevHome, but was {paths.Root}");

    var defaults = new MachineDefaultsService(paths);
    Check(PathsEqual(defaults.DefaultGamesRoot, @"C:\GrevCo\GrevHome\Games"),
        "Default Games folder must stay under C:\\GrevCo\\GrevHome.");
    Check(PathsEqual(defaults.DefaultBiosRoot, @"C:\GrevCo\GrevHome\Bios"),
        "Default BIOS folder must stay under C:\\GrevCo\\GrevHome.");

    var preferenceRoot = Path.Combine(Path.GetTempPath(), "GrevHomeInstallerPreferences-" + Guid.NewGuid().ToString("N"));
    var preferencePaths = new AppPaths(preferenceRoot);
    Directory.CreateDirectory(preferencePaths.Data);
    await File.WriteAllTextAsync(
        Path.Combine(preferencePaths.Data, "installer-first-run.ini"),
        "[Setup]\nGamesRoot=D:\\Games\nBiosRoot=D:\\BIOS\nEmulatorSetup=True\nConsoles=Nintendo - Nintendo DS|Microsoft - Original Xbox\n");
    var preferences = InstallerFirstRunPreferences.Load(preferencePaths, @"C:\fallback-games", @"C:\fallback-bios");
    Check(PathsEqual(preferences.GamesRoot, @"D:\Games") && PathsEqual(preferences.BiosRoot, @"D:\BIOS"),
        "Installer folder choices must reach first-run setup.");
    Check(preferences.EmulatorSetup && preferences.Consoles.Count == 2,
        "Installer emulator and console choices must reach first-run setup.");
    Directory.Delete(preferenceRoot, recursive: true);

    var alternateDriveRoot = AppPaths.GetStandardRootForDrive(@"D:\");
    Check(PathsEqual(alternateDriveRoot, @"D:\GrevCo\GrevHome"),
        "Drive choices must use <drive>:\\GrevCo\\GrevHome, never <drive>:\\GrevHome.");

    Console.WriteLine("Install layout tests passed: Grev-owned defaults and installer choices are handed off correctly.");
}
finally
{
    Environment.SetEnvironmentVariable("GREV_HOME_ROOT", previousOverride);
}

static bool PathsEqual(string left, string right) =>
    string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
