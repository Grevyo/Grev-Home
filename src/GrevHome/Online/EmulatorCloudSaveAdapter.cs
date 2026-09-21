using System.Diagnostics;
using GrevHome.Storage;

namespace GrevHome.Online;

public sealed record CloudSaveCoverage(string Summary, string? Warning = null);

/// <summary>
/// Mirrors emulator-specific save locations into the canonical per-profile Saves folder used by
/// the transport service, then applies a restored mirror back to those locations. A capture is
/// transactional: a locked/corrupt source fails the operation instead of uploading a partial set.
/// </summary>
public sealed class EmulatorCloudSaveAdapter
{
    private sealed record Source(string ArchiveName, Func<AppPaths, string, string> Resolve);
    private sealed record Layout(string ProcessName, string Summary, string? Warning, Source[] Sources);

    private static readonly IReadOnlyDictionary<string, Layout> Layouts =
        new Dictionary<string, Layout>(StringComparer.OrdinalIgnoreCase)
        {
            ["pcsx2"] = new("pcsx2-qt", "Memory cards and save states",
                null,
                [Data("MemoryCards", "memcards"), Data("SaveStates", "sstates")]),
            ["dolphin"] = new("Dolphin", "GameCube/Wii saves and save states",
                null,
                [Data("GameCube", "GC"), Data("Wii", "Wii"), Data("SaveStates", "StateSaves")]),
            ["azahar"] = new("azahar", "3DS NAND, SD data and save states",
                null,
                [Binary("Nand", "user", "nand"), Binary("Sdmc", "user", "sdmc"), Binary("States", "user", "states")]),
            ["rpcs3"] = new("rpcs3", "PS3 user saves, trophies and user data",
                null,
                [Binary("Home", "dev_hdd0", "home")]),
            ["cemu"] = new("Cemu", "Wii U user saves",
                null,
                [Binary("UserSaves", "mlc01", "usr", "save")]),
            ["xenia"] = new("xenia_canary", "Xbox 360 content and saves",
                "Xenia stores saves together with title content. Grev Home includes that folder, but the 300 MB cloud limit may require removing large installed content before sync.",
                [Binary("Content", "content")]),
            ["xemu"] = new("xemu", "EEPROM and separate memory-unit data",
                "Partial coverage: saves written inside xemu's virtual hard-disk image cannot be safely separated from the full disk. Grev Home syncs EEPROM and memory-unit data only and will always show this warning.",
                [Binary("Eeprom", "eeprom.bin"), Binary("MemoryUnits", "memory_units")]),
            ["vita3k"] = new("Vita3K", "PS Vita user saves, trophies and user data",
                null,
                [Data("User", "ux0", "user")])
        };

    private readonly AppPaths _paths;

    public EmulatorCloudSaveAdapter(AppPaths paths) => _paths = paths;

    public CloudSaveCoverage GetCoverage(string appId) => Layouts.TryGetValue(appId, out var layout)
        ? new CloudSaveCoverage(layout.Summary, layout.Warning)
        : new CloudSaveCoverage("All data in this app's Grev Home save folder");

    public async Task CaptureAsync(string grevId, string appId, CancellationToken cancellationToken = default)
    {
        if (!Layouts.TryGetValue(appId, out var layout)) return;
        EnsureNotRunning(layout);

        var saveRoot = _paths.GetProfileAppSaves(grevId, appId);
        var savesRoot = _paths.GetProfileSaves(grevId);
        Directory.CreateDirectory(savesRoot);
        var staging = Path.Combine(savesRoot, $".capture-{appId}-{Guid.NewGuid():N}");
        var previous = Path.Combine(savesRoot, $".previous-{appId}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);
            foreach (var source in layout.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = source.Resolve(_paths, grevId);
                var destination = Path.Combine(staging, source.ArchiveName);
                if (Directory.Exists(sourcePath))
                    await CopyDirectoryAsync(sourcePath, destination, cancellationToken);
                else if (File.Exists(sourcePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(sourcePath, destination, overwrite: true);
                }
            }

            if (Directory.Exists(saveRoot)) Directory.Move(saveRoot, previous);
            try
            {
                Directory.Move(staging, saveRoot);
            }
            catch
            {
                if (Directory.Exists(previous) && !Directory.Exists(saveRoot)) Directory.Move(previous, saveRoot);
                throw;
            }
            TryDeleteDirectory(previous);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public async Task ApplyRestoreAsync(string grevId, string appId, CancellationToken cancellationToken = default)
    {
        if (!Layouts.TryGetValue(appId, out var layout)) return;
        EnsureNotRunning(layout);
        var saveRoot = _paths.GetProfileAppSaves(grevId, appId);

        foreach (var source in layout.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captured = Path.Combine(saveRoot, source.ArchiveName);
            if (!Directory.Exists(captured) && !File.Exists(captured)) continue;
            var destination = source.Resolve(_paths, grevId);
            await ReplaceTargetAsync(captured, destination, cancellationToken);
        }
    }

    private static void EnsureNotRunning(Layout layout)
    {
        if (Process.GetProcessesByName(layout.ProcessName).Any())
            throw new IOException($"Close {layout.ProcessName} before syncing or restoring its cloud save so every file is complete.");
    }

    private static async Task ReplaceTargetAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var staging = destination + $".grev-staging-{Guid.NewGuid():N}";
        var backup = destination + $".grev-backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        try
        {
            if (Directory.Exists(source)) await CopyDirectoryAsync(source, staging, cancellationToken);
            else File.Copy(source, staging, overwrite: false);

            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            else if (File.Exists(destination)) File.Move(destination, backup);

            try
            {
                if (Directory.Exists(staging)) Directory.Move(staging, destination);
                else File.Move(staging, destination);
            }
            catch
            {
                if (!Directory.Exists(destination) && !File.Exists(destination))
                {
                    if (Directory.Exists(backup)) Directory.Move(backup, destination);
                    else if (File.Exists(backup)) File.Move(backup, destination);
                }
                throw;
            }
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var output = new FileStream(Path.Combine(destination, Path.GetFileName(file)), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Cloud save capture stopped at unsupported linked folder: {directory}");
            await CopyDirectoryAsync(directory, Path.Combine(destination, Path.GetFileName(directory)), cancellationToken);
        }
    }

    private static Source Data(string archiveName, params string[] relative) =>
        new(archiveName, (paths, grevId) => Combine(paths.GetProfileAppDataRoot(grevId, CurrentApp(relative)), relative));

    private static Source Binary(string archiveName, params string[] relative) =>
        new(archiveName, (paths, grevId) => Combine(paths.GetProfileAppRoot(grevId, CurrentApp(relative)), relative));

    private static string Combine(string root, string[] relative)
    {
        var parts = new string[relative.Length + 1];
        parts[0] = root;
        Array.Copy(relative, 0, parts, 1, relative.Length);
        return Path.Combine(parts);
    }

    // Source resolvers need their owning app id. It is encoded as the first hidden segment by the
    // helpers below at dictionary construction time and removed before Path.Combine is reached.
    private static string CurrentApp(string[] relative) => relative[0] switch
    {
        "memcards" or "sstates" => "pcsx2",
        "GC" or "Wii" or "StateSaves" => "dolphin",
        "ux0" => "vita3k",
        "user" => "azahar",
        "dev_hdd0" => "rpcs3",
        "mlc01" => "cemu",
        "content" => "xenia",
        "eeprom.bin" or "memory_units" => "xemu",
        _ => throw new InvalidOperationException("Unknown emulator cloud-save source.")
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path) => TryDelete(path);
}
