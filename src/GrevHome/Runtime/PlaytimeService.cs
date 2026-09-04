using System.IO;
using System.Text.Json;
using GrevHome.Sessions;
using GrevHome.Storage;

namespace GrevHome.Runtime;

public sealed record AppPlaytimeStat(
    string AppId,
    string AppName,
    long TotalSeconds,
    int SessionCount,
    DateTimeOffset LastPlayedAtUtc);

public sealed record PlaytimeSnapshot(
    int SchemaVersion,
    IReadOnlyDictionary<string, AppPlaytimeStat> Apps,
    IReadOnlyList<Guid>? AppliedSessionIds = null,
    int UniqueAppsFloor = 0);

public sealed class PlaytimeService
{
    private const int SchemaVersion = 2;

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public PlaytimeService(AppPaths paths)
    {
        _paths = paths;
    }

    public Task RecordSessionAsync(
        string appId,
        string appName,
        IReadOnlyList<LaunchParticipant> participants,
        TimeSpan duration,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default) =>
        RecordSessionAsync(
            Guid.Empty,
            appId,
            appName,
            participants,
            duration,
            endedAtUtc,
            cancellationToken);

    public async Task RecordSessionAsync(
        Guid sessionId,
        string appId,
        string appName,
        IReadOnlyList<LaunchParticipant> participants,
        TimeSpan duration,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var seconds = Math.Max(0L, (long)Math.Round(duration.TotalSeconds));
        var targets = participants
            .GroupBy(participant => participant.GrevId ?? participant.AccountKind.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var participant in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ResolveStatsPath(participant);
                if (path is null)
                {
                    continue;
                }

                var snapshot = await ReadAsync(path, cancellationToken);
                var applied = (snapshot.AppliedSessionIds ?? Array.Empty<Guid>())
                    .Where(id => id != Guid.Empty)
                    .ToHashSet();

                // The aggregate and its applied-session receipt are written in one atomic JSON
                // replacement. Replaying a durable completion after a crash can therefore never
                // increment the same runtime session twice.
                if (sessionId != Guid.Empty && applied.Contains(sessionId))
                {
                    continue;
                }

                var apps = new Dictionary<string, AppPlaytimeStat>(snapshot.Apps, StringComparer.OrdinalIgnoreCase);

                if (apps.TryGetValue(appId, out var existing))
                {
                    apps[appId] = existing with
                    {
                        AppName = appName,
                        TotalSeconds = checked(existing.TotalSeconds + seconds),
                        SessionCount = checked(existing.SessionCount + 1),
                        LastPlayedAtUtc = endedAtUtc
                    };
                }
                else
                {
                    apps[appId] = new AppPlaytimeStat(
                        appId,
                        appName,
                        seconds,
                        1,
                        endedAtUtc);
                }

                if (sessionId != Guid.Empty)
                {
                    applied.Add(sessionId);
                }

                await WriteAsync(
                    path,
                    new PlaytimeSnapshot(
                        SchemaVersion,
                        apps,
                        applied.OrderBy(id => id).ToArray()),
                    cancellationToken);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<PlaytimeSnapshot> GetForGrevIdAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        var local = await GetLocalForGrevIdAsync(grevId,cancellationToken);
        return await GrevHome.Online.GrevDadAccountDataStore.CombineAsync(_paths,grevId,local,cancellationToken);
    }

    public async Task<PlaytimeSnapshot> GetLocalForGrevIdAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureProfileLayout(grevId);
        return await ReadAsync(_paths.GetProfilePlaytimeFile(grevId), cancellationToken);
    }

    private string? ResolveStatsPath(LaunchParticipant participant)
    {
        if (!string.IsNullOrWhiteSpace(participant.GrevId))
        {
            _paths.EnsureProfileLayout(participant.GrevId);
            return _paths.GetProfilePlaytimeFile(participant.GrevId);
        }

        if (participant.AccountKind == AccountKind.Guest)
        {
            _paths.EnsureGuestLayout();
            return _paths.GuestPlaytimeFile;
        }

        return null;
    }

    private async Task<PlaytimeSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return EmptySnapshot();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<PlaytimeSnapshot>(stream, _jsonOptions, cancellationToken);
            if (snapshot is null || snapshot.Apps is null)
            {
                return RecoverMalformedSnapshot(path, "Playtime JSON contained no usable snapshot.");
            }

            if (snapshot.SchemaVersion > SchemaVersion)
            {
                // Never let an older Grev Home build overwrite data written by a newer schema.
                throw new InvalidDataException(
                    $"Playtime data schema {snapshot.SchemaVersion} is newer than this build supports ({SchemaVersion}).");
            }
            if (snapshot.SchemaVersion <= 0)
            {
                return RecoverMalformedSnapshot(path, $"Invalid playtime schema version {snapshot.SchemaVersion}.");
            }

            // Schema 1 contained only aggregate app totals. Preserve those totals exactly and begin
            // crash-safe SessionId receipts from this build onward; legacy sessions are never guessed.
            return new PlaytimeSnapshot(
                SchemaVersion,
                new Dictionary<string, AppPlaytimeStat>(snapshot.Apps, StringComparer.OrdinalIgnoreCase),
                (snapshot.AppliedSessionIds ?? Array.Empty<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToArray());
        }
        catch (JsonException ex)
        {
            return RecoverMalformedSnapshot(path, $"Playtime JSON could not be parsed: {ex.Message}");
        }
    }

    private PlaytimeSnapshot RecoverMalformedSnapshot(string path, string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, path, "Playtime", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found malformed playtime data and could not preserve a recovery copy. The file was left untouched.");
        }

        return EmptySnapshot();
    }

    private static PlaytimeSnapshot EmptySnapshot() =>
        new(
            SchemaVersion,
            new Dictionary<string, AppPlaytimeStat>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<Guid>());

    private async Task WriteAsync(
        string path,
        PlaytimeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
