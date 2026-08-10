using GrevHome.Runtime;

namespace GrevHome.Profiles;

public sealed record ProfileTopAppStat(
    string AppId,
    string AppName,
    long TotalSeconds,
    int SessionCount,
    bool IsRunning);

public sealed record ProfileStatSourceSnapshot(
    string SourceId,
    string DisplayName,
    bool IsConnected,
    string Status,
    long TotalSeconds,
    int CompletedSessions,
    int ActiveSessions,
    int UniqueApps,
    DateTimeOffset? LastActivityAtUtc,
    IReadOnlyList<ProfileTopAppStat> TopApps);

public sealed record ProfileLevelProgress(
    int Level,
    long TotalXp,
    long XpIntoLevel,
    long XpRequiredForNextLevel,
    double ProgressPercent);

public sealed record ProfileStatsSnapshot(
    ProfileLevelProgress Progression,
    long TotalTrackedSeconds,
    int CompletedSessions,
    int ActiveSessions,
    int UniqueApps,
    DateTimeOffset? LastActivityAtUtc,
    IReadOnlyList<ProfileTopAppStat> TopApps,
    IReadOnlyList<ProfileStatSourceSnapshot> Sources);

public sealed record ProfileStatsContext(
    string GrevId,
    IReadOnlyList<LaunchSessionSnapshot> ActiveRuntimeSessions);

public interface IProfileStatsSource
{
    string SourceId { get; }
    string DisplayName { get; }
    Task<ProfileStatSourceSnapshot> ReadAsync(
        ProfileStatsContext context,
        CancellationToken cancellationToken = default);
}

public sealed class GrevHomeProfileStatsSource : IProfileStatsSource
{
    private readonly PlaytimeService _playtime;

    public string SourceId => "grev-home";
    public string DisplayName => "Grev Home";

    public GrevHomeProfileStatsSource(PlaytimeService playtime)
    {
        _playtime = playtime;
    }

    public async Task<ProfileStatSourceSnapshot> ReadAsync(
        ProfileStatsContext context,
        CancellationToken cancellationToken = default)
    {
        var stored = await _playtime.GetForGrevIdAsync(context.GrevId, cancellationToken);
        var active = context.ActiveRuntimeSessions
            .Where(session => session.Participants.Any(participant =>
                string.Equals(participant.GrevId, context.GrevId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var combined = stored.Apps.Values.ToDictionary(
            app => app.AppId,
            app => new MutableAppStat(app.AppId, app.AppName, app.TotalSeconds, app.SessionCount, false),
            StringComparer.OrdinalIgnoreCase);

        foreach (var session in active)
        {
            var liveSeconds = Math.Max(0L, (long)Math.Floor(session.Elapsed.TotalSeconds));
            if (combined.TryGetValue(session.AppId, out var app))
            {
                app.AppName = session.AppName;
                app.TotalSeconds += liveSeconds;
                app.IsRunning = true;
            }
            else
            {
                combined[session.AppId] = new MutableAppStat(
                    session.AppId,
                    session.AppName,
                    liveSeconds,
                    0,
                    true);
            }
        }

        var completedSeconds = stored.Apps.Values.Sum(app => app.TotalSeconds);
        var activeSeconds = active.Sum(session => Math.Max(0L, (long)Math.Floor(session.Elapsed.TotalSeconds)));
        var lastStored = stored.Apps.Count == 0
            ? (DateTimeOffset?)null
            : stored.Apps.Values.Max(app => app.LastPlayedAtUtc);
        var lastActive = active.Length == 0
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow;
        var lastActivity = Max(lastStored, lastActive);

        var topApps = combined.Values
            .OrderByDescending(app => app.TotalSeconds)
            .ThenBy(app => app.AppName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(app => new ProfileTopAppStat(
                app.AppId,
                app.AppName,
                app.TotalSeconds,
                app.SessionCount,
                app.IsRunning))
            .ToArray();

        return new ProfileStatSourceSnapshot(
            SourceId,
            DisplayName,
            true,
            active.Length > 0
                ? $"Tracking {active.Length} running app{(active.Length == 1 ? string.Empty : "s")} now."
                : "Connected to Grev Home managed-app playtime.",
            completedSeconds + activeSeconds,
            stored.Apps.Values.Sum(app => app.SessionCount),
            active.Length,
            combined.Count,
            lastActivity,
            topApps);
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return left.Value >= right.Value ? left : right;
    }

    private sealed class MutableAppStat
    {
        public string AppId { get; }
        public string AppName { get; set; }
        public long TotalSeconds { get; set; }
        public int SessionCount { get; }
        public bool IsRunning { get; set; }

        public MutableAppStat(
            string appId,
            string appName,
            long totalSeconds,
            int sessionCount,
            bool isRunning)
        {
            AppId = appId;
            AppName = appName;
            TotalSeconds = totalSeconds;
            SessionCount = sessionCount;
            IsRunning = isRunning;
        }
    }
}

public sealed class ProfileStatsService
{
    private readonly IReadOnlyList<IProfileStatsSource> _sources;

    public ProfileStatsService(IEnumerable<IProfileStatsSource> sources)
    {
        _sources = sources.ToArray();
    }

    public async Task<ProfileStatsSnapshot> GetAsync(
        string grevId,
        IReadOnlyList<LaunchSessionSnapshot> activeRuntimeSessions,
        CancellationToken cancellationToken = default)
    {
        var context = new ProfileStatsContext(grevId, activeRuntimeSessions);
        var sources = new List<ProfileStatSourceSnapshot>();

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                sources.Add(await source.ReadAsync(context, cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                sources.Add(new ProfileStatSourceSnapshot(
                    source.SourceId,
                    source.DisplayName,
                    false,
                    $"Unavailable: {ex.Message}",
                    0,
                    0,
                    0,
                    0,
                    null,
                    Array.Empty<ProfileTopAppStat>()));
            }
        }

        var grevHome = sources.FirstOrDefault(source =>
            string.Equals(source.SourceId, "grev-home", StringComparison.OrdinalIgnoreCase));

        var totalSeconds = grevHome?.TotalSeconds ?? 0;
        var completedSessions = grevHome?.CompletedSessions ?? 0;
        var activeSessions = grevHome?.ActiveSessions ?? 0;
        var uniqueApps = grevHome?.UniqueApps ?? 0;

        // Grev Level deliberately uses Grev Home's own tracked activity only. External providers
        // can enrich/showcase a profile later without double-counting imported hours as progression.
        var xp = Math.Max(0L, totalSeconds / 60) +
                 (long)completedSessions * 20L +
                 (long)uniqueApps * 100L;

        return new ProfileStatsSnapshot(
            CalculateLevel(xp),
            totalSeconds,
            completedSessions,
            activeSessions,
            uniqueApps,
            grevHome?.LastActivityAtUtc,
            grevHome?.TopApps ?? Array.Empty<ProfileTopAppStat>(),
            sources);
    }

    public static ProfileLevelProgress CalculateLevel(long totalXp)
    {
        totalXp = Math.Max(0, totalXp);
        var level = 1;
        var remaining = totalXp;
        var requirement = XpRequiredForLevel(level);

        while (remaining >= requirement && level < 999)
        {
            remaining -= requirement;
            level++;
            requirement = XpRequiredForLevel(level);
        }

        var percent = requirement <= 0
            ? 100d
            : Math.Clamp(remaining * 100d / requirement, 0d, 100d);

        return new ProfileLevelProgress(
            level,
            totalXp,
            remaining,
            requirement,
            percent);
    }

    private static long XpRequiredForLevel(int currentLevel) =>
        250L + (Math.Max(1, currentLevel) - 1L) * 150L;
}
