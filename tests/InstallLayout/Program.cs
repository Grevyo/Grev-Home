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

    var alternateDriveRoot = AppPaths.GetStandardRootForDrive(@"D:\");
    Check(PathsEqual(alternateDriveRoot, @"D:\GrevCo\GrevHome"),
        "Drive choices must use <drive>:\\GrevCo\\GrevHome, never <drive>:\\GrevHome.");

    Console.WriteLine("Install layout tests passed: Grev-owned defaults are rooted under GrevCo/GrevHome.");
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
