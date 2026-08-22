using GrevHome.Runtime;

namespace GrevHome.Profiles;

public sealed record ProfileTopAppStat(
    string AppId,
    string AppName,
    long TotalSeconds,
    int SessionCount,
    bool IsRunning,
    DateTimeOffset LastActivityAtUtc);

public sealed record ProfileRecentActivityStat(
    string AppId,
    string AppName,
    long TotalSeconds,
    int SessionCount,
    bool IsRunning,
    DateTimeOffset LastActivityAtUtc);

public sealed record ProfileMilestoneStat(
    string MilestoneId,
    string Title,
    string Description,
    bool IsEarned,
    long ProgressValue,
    long TargetValue,
    string ProgressLabel);

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
    IReadOnlyList<ProfileTopAppStat> TopApps,
    IReadOnlyList<ProfileRecentActivityStat> RecentActivity);

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
    IReadOnlyList<ProfileRecentActivityStat> RecentActivity,
    IReadOnlyList<ProfileMilestoneStat> Milestones,
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
            app => new MutableAppStat(
                app.AppId,
                app.AppName,
                app.TotalSeconds,
                app.SessionCount,
                false,
                app.LastPlayedAtUtc),
            StringComparer.OrdinalIgnoreCase);

        foreach (var session in active)
        {
            var liveSeconds = Math.Max(0L, (long)Math.Floor(session.Elapsed.TotalSeconds));
            if (combined.TryGetValue(session.AppId, out var app))
            {
                app.AppName = session.AppName;
                app.TotalSeconds += liveSeconds;
                app.IsRunning = true;
                app.LastActivityAtUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                combined[session.AppId] = new MutableAppStat(
                    session.AppId,
                    session.AppName,
                    liveSeconds,
                    0,
                    true,
                    DateTimeOffset.UtcNow);
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
                app.IsRunning,
                app.LastActivityAtUtc))
            .ToArray();

        var recentActivity = combined.Values
            .OrderByDescending(app => app.LastActivityAtUtc)
            .ThenBy(app => app.AppName, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(app => new ProfileRecentActivityStat(
                app.AppId,
                app.AppName,
                app.TotalSeconds,
                app.SessionCount,
                app.IsRunning,
                app.LastActivityAtUtc))
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
            topApps,
            recentActivity);
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
        public DateTimeOffset LastActivityAtUtc { get; set; }

        public MutableAppStat(
            string appId,
            string appName,
            long totalSeconds,
            int sessionCount,
            bool isRunning,
            DateTimeOffset lastActivityAtUtc)
        {
            AppId = appId;
            AppName = appName;
            TotalSeconds = totalSeconds;
            SessionCount = sessionCount;
            IsRunning = isRunning;
            LastActivityAtUtc = lastActivityAtUtc;
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
                    Array.Empty<ProfileTopAppStat>(),
                    Array.Empty<ProfileRecentActivityStat>()));
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
        var xp = GrevHomeProgressionPolicy.CalculateXp(totalSeconds, completedSessions, uniqueApps);
        var progression = CalculateLevel(xp);

        return new ProfileStatsSnapshot(
            progression,
            totalSeconds,
            completedSessions,
            activeSessions,
            uniqueApps,
            grevHome?.LastActivityAtUtc,
            grevHome?.TopApps ?? Array.Empty<ProfileTopAppStat>(),
            grevHome?.RecentActivity ?? Array.Empty<ProfileRecentActivityStat>(),
            CalculateMilestones(totalSeconds, completedSessions, uniqueApps, progression.Level),
            sources);
    }

    public static ProfileLevelProgress CalculateLevel(long totalXp) =>
        GrevHomeProgressionPolicy.CalculateLevel(totalXp);

    private static IReadOnlyList<ProfileMilestoneStat> CalculateMilestones(
        long totalSeconds,
        int completedSessions,
        int uniqueApps,
        int level)
    {
        var totalHours = totalSeconds / 3600L;
        return new[]
        {
            Milestone("first-session", "First Boot", "Complete your first managed app session.", completedSessions, 1, $"{Math.Min(completedSessions, 1)}/1 session"),
            Milestone("one-hour", "Settling In", "Track one hour in Grev Home.", totalHours, 1, $"{Math.Min(totalHours, 1)}/1 hour"),
            Milestone("ten-hours", "Regular", "Track ten hours in Grev Home.", totalHours, 10, $"{Math.Min(totalHours, 10)}/10 hours"),
            Milestone("hundred-hours", "Centurion", "Track one hundred hours in Grev Home.", totalHours, 100, $"{Math.Min(totalHours, 100)}/100 hours"),
            Milestone("five-apps", "Explorer", "Use five different managed apps.", uniqueApps, 5, $"{Math.Min(uniqueApps, 5)}/5 apps"),
            Milestone("twenty-apps", "Library Hopper", "Use twenty different managed apps.", uniqueApps, 20, $"{Math.Min(uniqueApps, 20)}/20 apps"),
            Milestone("fifty-sessions", "Session Veteran", "Complete fifty managed app sessions.", completedSessions, 50, $"{Math.Min(completedSessions, 50)}/50 sessions"),
            Milestone("level-five", "Level Five", "Reach Grev Level 5.", level, 5, $"Level {Math.Min(level, 5)}/5"),
            Milestone("level-ten", "Double Digits", "Reach Grev Level 10.", level, 10, $"Level {Math.Min(level, 10)}/10")
        };
    }

    private static ProfileMilestoneStat Milestone(
        string id,
        string title,
        string description,
        long progress,
        long target,
        string progressLabel) =>
        new(id, title, description, progress >= target, Math.Min(progress, target), target, progressLabel);
}
