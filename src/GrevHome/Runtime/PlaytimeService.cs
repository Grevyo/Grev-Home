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
    IReadOnlyDictionary<string, AppPlaytimeStat> Apps);

public sealed class PlaytimeService
{
    private const int SchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public PlaytimeService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task RecordSessionAsync(
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
                var path = ResolveStatsPath(participant);
                if (path is null)
                {
                    continue;
                }

                var snapshot = await ReadAsync(path, cancellationToken);
                var apps = new Dictionary<string, AppPlaytimeStat>(snapshot.Apps, StringComparer.OrdinalIgnoreCase);

                if (apps.TryGetValue(appId, out var existing))
                {
                    apps[appId] = existing with
                    {
                        AppName = appName,
                        TotalSeconds = existing.TotalSeconds + seconds,
                        SessionCount = existing.SessionCount + 1,
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

                await WriteAsync(path, new PlaytimeSnapshot(SchemaVersion, apps), cancellationToken);
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
            return new PlaytimeSnapshot(
                SchemaVersion,
                new Dictionary<string, AppPlaytimeStat>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<PlaytimeSnapshot>(stream, _jsonOptions, cancellationToken);
            return snapshot ?? new PlaytimeSnapshot(
                SchemaVersion,
                new Dictionary<string, AppPlaytimeStat>(StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return new PlaytimeSnapshot(
                SchemaVersion,
                new Dictionary<string, AppPlaytimeStat>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task WriteAsync(
        string path,
        PlaytimeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions, cancellationToken);
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
