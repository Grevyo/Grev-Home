using System.IO;
using System.Text.Json;

namespace GrevHome.Storage;

public sealed record LegacyDataImportResult(
    bool Imported,
    string? SourceRoot,
    int FilesCopied,
    string? Message);

/// <summary>
/// One-time compatibility import for builds that stored Grev Home state directly under C:\GrevHome
/// before the product root was standardised as C:\GrevCo\GrevHome.
///
/// The importer is deliberately conservative:
/// - it only runs for the canonical root and never when GREV_HOME_ROOT is explicitly set;
/// - it requires recognisable Grev Home state (machine-defaults.json or a real profile.json);
/// - it refuses to import over an already-configured canonical root;
/// - it copies persistent application/profile state, never source-code folders and never the large
///   Games/Bios folders themselves;
/// - the legacy tree is left untouched as a rollback copy.
///
/// Existing machine-defaults paths are preserved. So if an older setup intentionally points at
/// C:\GrevHome\Games, Grev Home keeps using that library rather than silently copying gigabytes.
/// </summary>
public sealed class LegacyDataMigrationService
{
    public const string LegacyRoot = @"C:\GrevHome";

    private static readonly string[] PersistentDirectories =
        ["Data", "Profiles", "Global", "Themes", "Packages"];

    private static readonly string[] PersistentRootFiles =
        ["FileFavorites.json"];

    private readonly AppPaths _paths;

    public LegacyDataMigrationService(AppPaths paths)
    {
        _paths = paths;
    }

    public string MarkerFile => Path.Combine(_paths.Data, "Migrations", "legacy-c-grevhome-import.json");

    public async Task<LegacyDataImportResult> ImportIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!ShouldConsiderImport())
        {
            return new LegacyDataImportResult(false, null, 0, null);
        }

        if (!HasRecognisableLegacyState(LegacyRoot))
        {
            return new LegacyDataImportResult(false, LegacyRoot, 0, null);
        }

        if (HasMeaningfulCanonicalState())
        {
            return new LegacyDataImportResult(
                false,
                LegacyRoot,
                0,
                "Legacy Grev Home data was detected, but the canonical GrevCo data root already contains configured state, so nothing was imported automatically.");
        }

        var copied = 0;
        foreach (var directoryName in PersistentDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(LegacyRoot, directoryName);
            if (!Directory.Exists(source)) continue;
            copied += CopyDirectory(source, Path.Combine(_paths.Root, directoryName), cancellationToken);
        }

        foreach (var fileName in PersistentRootFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(LegacyRoot, fileName);
            if (!File.Exists(source)) continue;
            var target = Path.Combine(_paths.Root, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) continue;
            File.Copy(source, target, overwrite: false);
            copied++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MarkerFile)!);
        var marker = new
        {
            sourceRoot = LegacyRoot,
            targetRoot = _paths.Root,
            importedAtUtc = DateTimeOffset.UtcNow,
            filesCopied = copied,
            legacySourcePreserved = true
        };
        await File.WriteAllTextAsync(
            MarkerFile,
            JsonSerializer.Serialize(marker, JsonDefaults.IndentedWeb),
            cancellationToken);

        return new LegacyDataImportResult(
            true,
            LegacyRoot,
            copied,
            $"Imported {copied} legacy Grev Home data file(s) from {LegacyRoot}. The old folder was left untouched.");
    }

    private bool ShouldConsiderImport()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GREV_HOME_ROOT"))) return false;
        if (!string.Equals(
                Path.GetFullPath(_paths.Root).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(AppPaths.StandardRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) return false;
        if (!Directory.Exists(LegacyRoot)) return false;
        return !File.Exists(MarkerFile);
    }

    private bool HasMeaningfulCanonicalState()
    {
        if (File.Exists(Path.Combine(_paths.Data, "machine-defaults.json"))) return true;
        if (!Directory.Exists(_paths.Profiles)) return false;

        return Directory.EnumerateDirectories(_paths.Profiles)
            .Where(path => !string.Equals(Path.GetFileName(path), "_GuestShared", StringComparison.OrdinalIgnoreCase))
            .Any(path => File.Exists(Path.Combine(path, "profile.json")));
    }

    private static bool HasRecognisableLegacyState(string root)
    {
        if (File.Exists(Path.Combine(root, "Data", "machine-defaults.json"))) return true;

        var profiles = Path.Combine(root, "Profiles");
        if (!Directory.Exists(profiles)) return false;
        return Directory.EnumerateDirectories(profiles)
            .Where(path => !string.Equals(Path.GetFileName(path), "_GuestShared", StringComparison.OrdinalIgnoreCase))
            .Any(path => File.Exists(Path.Combine(path, "profile.json")));
    }

    private static int CopyDirectory(string sourceRoot, string targetRoot, CancellationToken cancellationToken)
    {
        var copied = 0;
        Directory.CreateDirectory(targetRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) continue;
            File.Copy(file, target, overwrite: false);
            copied++;
        }

        return copied;
    }
}
