using GrevHome.Storage;

var previousOverride = Environment.GetEnvironmentVariable("GREV_HOME_ROOT");
var testRoot = Path.Combine(Path.GetTempPath(), "GrevHome-InstallLayout-" + Guid.NewGuid().ToString("N"));
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

    // Multiple Games locations are machine-wide library roots, with the primary first. This uses a
    // temporary custom AppPaths root so the test never touches the runner's real C:\GrevCo tree.
    var testPaths = new AppPaths(testRoot);
    testPaths.EnsureMachineLayout();
    var testDefaults = new MachineDefaultsService(testPaths);
    var primaryGames = Path.Combine(testRoot, "Libraries", "Primary");
    var extraOne = Path.Combine(testRoot, "Libraries", "Arcade");
    var extraTwo = Path.Combine(testRoot, "Libraries", "Retro");
    var bios = Path.Combine(testRoot, "Firmware");

    await testDefaults.SaveAsync(primaryGames, bios, [extraOne, extraTwo, extraOne]);
    var saved = await testDefaults.GetAsync();
    Check(saved.SetupCompleted, "Saving machine library locations must complete first-run setup.");
    Check(saved.AdditionalGamesRoots?.Count == 2, "Additional Games roots must be persisted and de-duplicated.");

    var roots = await testDefaults.GetGamesRootsAsync();
    Check(roots.Count == 3, "GetGamesRootsAsync must return primary plus every additional Games location.");
    Check(PathsEqual(roots[0], primaryGames), "The primary Games root must remain first.");
    Check(roots.Skip(1).Any(path => PathsEqual(path, extraOne)) && roots.Skip(1).Any(path => PathsEqual(path, extraTwo)),
        "Every configured additional Games location must round-trip through machine defaults.");

    Console.WriteLine("Install layout tests passed: Grev-owned defaults stay under GrevCo/GrevHome and multiple Games roots round-trip safely.");
}
finally
{
    Environment.SetEnvironmentVariable("GREV_HOME_ROOT", previousOverride);
    try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true); } catch { }
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
