using System.IO;
using System.Text.Json;
using GrevHome.Profiles;
using GrevHome.Storage;

namespace GrevHome.Diagnostics;

public enum MachineHealthStatus
{
    Healthy,
    Warning,
    Error
}

public sealed record MachineHealthCheck(
    string Id,
    MachineHealthStatus Status,
    string Message);

public sealed record MachineHealthSnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string EffectiveRoot,
    long? FreeDiskBytes,
    int ProfileDirectoryCount,
    int ReadableProfileCount,
    int AdminProfileCount,
    int LinkedGrevDadMetadataCount,
    int RecoveryArtifactCount,
    int StaleTemporaryArtifactCount,
    int PendingRuntimeCompletionCount,
    IReadOnlyList<MachineHealthCheck> Checks)
{
    public MachineHealthStatus OverallStatus =>
        Checks.Any(check => check.Status == MachineHealthStatus.Error)
            ? MachineHealthStatus.Error
            : Checks.Any(check => check.Status == MachineHealthStatus.Warning)
                ? MachineHealthStatus.Warning
                : MachineHealthStatus.Healthy;
}

/// <summary>
/// Non-destructive local appliance health snapshot used by diagnostics and the later full-system
/// test. It never repairs, upgrades or quarantines data and never requires Grev.dad/network access.
/// </summary>
public sealed class MachineHealthService
{
    private const int SchemaVersion = 2;
    private const int StaleTemporaryMinutes = 30;

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public MachineHealthService(AppPaths paths)
    {
        _paths = paths;
    }

    public string DiagnosticsRoot => Path.Combine(_paths.Data, "Diagnostics");
    public string LatestSnapshotFile => Path.Combine(DiagnosticsRoot, "machine-health-latest.json");
    public string SnapshotHistoryFile => Path.Combine(DiagnosticsRoot, "machine-health-history.jsonl");

    public async Task<MachineHealthSnapshot> CaptureAndPersistAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureAsync(cancellationToken);
        await PersistAsync(snapshot, cancellationToken);
        return snapshot;
    }

    public async Task<MachineHealthSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureMachineLayout();
        var checks = new List<MachineHealthCheck>();

        CheckRootWriteability(checks);
        var freeBytes = ReadFreeDiskBytes(checks);
        CheckLocalSchema(checks);

        var profileDirectories = EnumerateProfileDirectories(checks);
        var readableProfiles = await ReadProfilesForHealthAsync(profileDirectories, checks, cancellationToken);

        if (readableProfiles.Count != profileDirectories.Count)
        {
            checks.Add(new MachineHealthCheck(
                "profiles.metadata",
                MachineHealthStatus.Warning,
                $"Found {profileDirectories.Count} persistent profile folders but only {readableProfiles.Count} valid readable profile metadata records."));
        }
        else
        {
            checks.Add(new MachineHealthCheck(
                "profiles.metadata",
                MachineHealthStatus.Healthy,
                $"All {readableProfiles.Count} persistent profile metadata records are readable and structurally valid."));
        }

        var adminCount = readableProfiles.Count(profile => profile.Role == AccountRole.Admin);
        checks.Add(new MachineHealthCheck(
            "profiles.admin",
            adminCount > 0 ? MachineHealthStatus.Healthy : MachineHealthStatus.Error,
            adminCount > 0
                ? $"{adminCount} local Admin profile{(adminCount == 1 ? string.Empty : "s")} available."
                : "No readable local Admin profile was found."));

        var duplicateGrevIds = readableProfiles
            .GroupBy(profile => profile.GrevId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var duplicateUsernames = readableProfiles
            .GroupBy(profile => profile.Username, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        checks.Add(new MachineHealthCheck(
            "profiles.identity",
            duplicateGrevIds.Length == 0 && duplicateUsernames.Length == 0
                ? MachineHealthStatus.Healthy
                : MachineHealthStatus.Error,
            duplicateGrevIds.Length == 0 && duplicateUsernames.Length == 0
                ? "Persistent GrevID and Username identities are unique."
                : $"Duplicate identities detected. GrevIDs={string.Join(",", duplicateGrevIds)}; Usernames={string.Join(",", duplicateUsernames)}"));

        var linkedMetadataCount = readableProfiles.Count(profile =>
            File.Exists(Path.Combine(_paths.GetProfileConnections(profile.GrevId), "GrevDad", "link.json")));
        checks.Add(new MachineHealthCheck(
            "grevdad.optional-links",
            MachineHealthStatus.Healthy,
            $"{linkedMetadataCount} profile{(linkedMetadataCount == 1 ? string.Empty : "s")} contain local Grev.dad link metadata. Network availability is not part of this health check."));

        var recoveryCount = CountFilesSafely(Path.Combine(_paths.Data, "Recovery"), "*.corrupt", checks, "recovery.scan");
        checks.Add(new MachineHealthCheck(
            "recovery.artifacts",
            recoveryCount == 0 ? MachineHealthStatus.Healthy : MachineHealthStatus.Warning,
            recoveryCount == 0
                ? "No quarantined corrupt-data artifacts are present."
                : $"{recoveryCount} quarantined corrupt-data artifact{(recoveryCount == 1 ? string.Empty : "s")} are preserved for diagnosis."));

        var pendingCompletionCount = CountFilesSafely(
            Path.Combine(_paths.RuntimeData, "PendingCompletions"),
            "*.json",
            checks,
            "runtime.pending-scan");
        checks.Add(new MachineHealthCheck(
            "runtime.pending-completions",
            pendingCompletionCount == 0 ? MachineHealthStatus.Healthy : MachineHealthStatus.Warning,
            pendingCompletionCount == 0
                ? "No deferred runtime completion commits are waiting for replay."
                : $"{pendingCompletionCount} exact runtime completion{(pendingCompletionCount == 1 ? string.Empty : "s")} remain queued for local playtime/history replay."));

        var staleTemporaryCount = CountStaleTemporaryFiles(checks);
        checks.Add(new MachineHealthCheck(
            "storage.temporary-files",
            staleTemporaryCount == 0 ? MachineHealthStatus.Healthy : MachineHealthStatus.Warning,
            staleTemporaryCount == 0
                ? "No stale Grev Home temporary-write files were found."
                : $"{staleTemporaryCount} .tmp file{(staleTemporaryCount == 1 ? string.Empty : "s")} older than {StaleTemporaryMinutes} minutes were found."));

        return new MachineHealthSnapshot(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            _paths.Root,
            freeBytes,
            profileDirectories.Count,
            readableProfiles.Count,
            adminCount,
            linkedMetadataCount,
            recoveryCount,
            staleTemporaryCount,
            pendingCompletionCount,
            checks);
    }

    private async Task<IReadOnlyList<LocalProfile>> ReadProfilesForHealthAsync(
        IReadOnlyList<string> profileDirectories,
        List<MachineHealthCheck> checks,
        CancellationToken cancellationToken)
    {
        var readable = new List<LocalProfile>();
        foreach (var directory in profileDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, "profile.json");
            if (!File.Exists(metadataPath))
            {
                checks.Add(new MachineHealthCheck(
                    $"profile.{Path.GetFileName(directory)}",
                    MachineHealthStatus.Warning,
                    $"Profile folder {Path.GetFileName(directory)} has no profile.json."));
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var profile = await JsonSerializer.DeserializeAsync<LocalProfile>(stream, _json, cancellationToken);
                var folder = Path.GetFileName(directory);
                if (profile is null ||
                    !string.Equals(profile.GrevId, folder, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(profile.Username) || profile.Username.Length > ProfileService.MaxUsernameLength ||
                    string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > ProfileService.MaxDisplayNameLength ||
                    !Enum.IsDefined(profile.Role))
                {
                    checks.Add(new MachineHealthCheck(
                        $"profile.{folder}",
                        MachineHealthStatus.Warning,
                        $"Profile metadata for {folder} failed structural identity validation."));
                    continue;
                }

                readable.Add(profile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                checks.Add(new MachineHealthCheck(
                    $"profile.{Path.GetFileName(directory)}",
                    MachineHealthStatus.Warning,
                    $"Profile metadata could not be read: {ex.Message}"));
            }
        }

        return readable;
    }

    private void CheckRootWriteability(List<MachineHealthCheck> checks)
    {
        var probeRoot = Path.Combine(_paths.Data, "Diagnostics");
        var probe = Path.Combine(probeRoot, $"write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(probeRoot);
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0x47);
                stream.Flush(flushToDisk: true);
            }
            File.Delete(probe);
            checks.Add(new MachineHealthCheck(
                "storage.write",
                MachineHealthStatus.Healthy,
                $"Grev Home data root is writable: {_paths.Root}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(new MachineHealthCheck(
                "storage.write",
                MachineHealthStatus.Error,
                $"Grev Home data root write probe failed: {ex.Message}"));
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    private long? ReadFreeDiskBytes(List<MachineHealthCheck> checks)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_paths.Root));
            if (string.IsNullOrWhiteSpace(root)) return null;
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            var status = free < 2L * 1024 * 1024 * 1024
                ? MachineHealthStatus.Warning
                : MachineHealthStatus.Healthy;
            checks.Add(new MachineHealthCheck(
                "storage.free-space",
                status,
                $"{free / (1024d * 1024d * 1024d):0.0} GB free on {drive.Name}."));
            return free;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            checks.Add(new MachineHealthCheck(
                "storage.free-space",
                MachineHealthStatus.Warning,
                $"Free disk space could not be determined: {ex.Message}"));
            return null;
        }
    }

    private void CheckLocalSchema(List<MachineHealthCheck> checks)
    {
        var stateFile = Path.Combine(_paths.Data, "Schema", "local-data-schema.json");
        try
        {
            if (!File.Exists(stateFile))
            {
                checks.Add(new MachineHealthCheck(
                    "storage.schema",
                    MachineHealthStatus.Warning,
                    "Local-data schema marker is missing."));
                return;
            }

            using var stream = File.OpenRead(stateFile);
            var state = JsonSerializer.Deserialize<LocalDataSchemaState>(stream, _json);
            if (state is null)
            {
                checks.Add(new MachineHealthCheck(
                    "storage.schema",
                    MachineHealthStatus.Error,
                    "Local-data schema marker is empty."));
                return;
            }

            var status = state.SchemaVersion == LocalDataSchemaService.CurrentSchemaVersion
                ? MachineHealthStatus.Healthy
                : MachineHealthStatus.Error;
            checks.Add(new MachineHealthCheck(
                "storage.schema",
                status,
                $"Local-data schema is {state.SchemaVersion}; this build supports {LocalDataSchemaService.CurrentSchemaVersion}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            checks.Add(new MachineHealthCheck(
                "storage.schema",
                MachineHealthStatus.Error,
                $"Local-data schema marker could not be read: {ex.Message}"));
        }
    }

    private IReadOnlyList<string> EnumerateProfileDirectories(List<MachineHealthCheck> checks)
    {
        try
        {
            return Directory.EnumerateDirectories(_paths.Profiles)
                .Where(path => !Path.GetFileName(path).StartsWith('_'))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(new MachineHealthCheck(
                "profiles.directories",
                MachineHealthStatus.Error,
                $"Profile directories could not be enumerated: {ex.Message}"));
            return Array.Empty<string>();
        }
    }

    private int CountStaleTemporaryFiles(List<MachineHealthCheck> checks)
    {
        try
        {
            var threshold = DateTime.UtcNow.AddMinutes(-StaleTemporaryMinutes);
            return Directory.EnumerateFiles(_paths.Root, "*.tmp", SearchOption.AllDirectories)
                .Take(1001)
                .Count(path =>
                {
                    try { return File.GetLastWriteTimeUtc(path) <= threshold; }
                    catch { return false; }
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(new MachineHealthCheck(
                "storage.temp-scan",
                MachineHealthStatus.Warning,
                $"Temporary-file scan was incomplete: {ex.Message}"));
            return 0;
        }
    }

    private static int CountFilesSafely(
        string root,
        string pattern,
        List<MachineHealthCheck> checks,
        string checkId)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Take(1001).Count()
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(new MachineHealthCheck(
                checkId,
                MachineHealthStatus.Warning,
                $"Scan was incomplete: {ex.Message}"));
            return 0;
        }
    }

    private async Task PersistAsync(MachineHealthSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DiagnosticsRoot);
        var temporary = LatestSnapshotFile + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, LatestSnapshotFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        await using var history = new FileStream(
            SnapshotHistoryFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(history);
        await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot, _json).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        history.Flush(flushToDisk: true);
    }
}
