namespace GrevHome.Profiles;

/// <summary>
/// Single progression authority for Grev Home. Local profile UI, milestones and optional Grev.dad
/// sync must all calculate XP/levels through this policy so a future balance change cannot make
/// two parts of the shell disagree about the same GrevID.
/// </summary>
public static class GrevHomeProgressionPolicy
{
    public const int MaximumLevel = 999;
    public const long XpPerTrackedMinute = 1;
    public const long XpPerCompletedSession = 20;
    public const long XpPerUniqueApp = 100;

    public static long CalculateXp(
        long totalTrackedSeconds,
        int completedSessions,
        int uniqueApps)
    {
        var trackedMinutes = Math.Max(0L, totalTrackedSeconds) / 60L;
        return checked(
            trackedMinutes * XpPerTrackedMinute +
            Math.Max(0L, (long)completedSessions) * XpPerCompletedSession +
            Math.Max(0L, (long)uniqueApps) * XpPerUniqueApp);
    }

    public static ProfileLevelProgress CalculateLevel(long totalXp)
    {
        totalXp = Math.Max(0, totalXp);
        var level = 1;
        var remaining = totalXp;
        var requirement = XpRequiredForLevel(level);

        while (remaining >= requirement && level < MaximumLevel)
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

    public static long XpRequiredForLevel(int currentLevel) =>
        250L + (Math.Max(1, currentLevel) - 1L) * 150L;
}
