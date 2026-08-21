using GrevHome.Apps;
using GrevHome.Runtime;
using GrevHome.Storage;

namespace GrevHome.Dashboard;

public sealed record DashboardAppActivity(
    string AppId,
    string AppName,
    long TotalSeconds,
    int SessionCount,
    DateTimeOffset LastPlayedAtUtc,
    bool IsInstalled,
    bool CanLaunch,
    string? AvailabilityMessage);

public sealed record DashboardDataSnapshot(
    long TotalPlaytimeSeconds,
    int TotalSessions,
    int AppsPlayed,
    DashboardAppActivity? ContinueApp,
    IReadOnlyList<DashboardAppActivity> RecentlyUsed)
{
    public static DashboardDataSnapshot Empty { get; } = new(
        0,
        0,
        0,
        null,
        Array.Empty<DashboardAppActivity>());
}

/// <summary>
/// Builds account-facing dashboard state from the existing Grev Home sources of truth.
/// It deliberately does not create a second activity database: completed runtime sessions are
/// already persisted by PlaytimeService, while launch availability comes from InstalledAppService.
/// </summary>
public sealed class DashboardDataService
{
    private readonly PlaytimeService _playtime;
    private readonly InstalledAppService _installedApps;

    public DashboardDataService(AppPaths paths, InstalledAppService installedApps)
    {
        _playtime = new PlaytimeService(paths);
        _installedApps = installedApps;
    }

    public async Task<DashboardDataSnapshot> GetForGrevIdAsync(
        string? grevId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return DashboardDataSnapshot.Empty;
        }

        var playtime = await _playtime.GetForGrevIdAsync(grevId, cancellationToken);
        var installed = await _installedApps.GetInstalledForUserAsync(grevId, cancellationToken);
        var installedByAppId = installed
            .GroupBy(entry => entry.Manifest.Definition.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(entry => entry.AvailableToCurrentUser).First(),
                StringComparer.OrdinalIgnoreCase);

        var recent = playtime.Apps.Values
            .OrderByDescending(stat => stat.LastPlayedAtUtc)
            .ThenBy(stat => stat.AppName, StringComparer.OrdinalIgnoreCase)
            .Select(stat =>
            {
                installedByAppId.TryGetValue(stat.AppId, out var installedEntry);
                return new DashboardAppActivity(
                    stat.AppId,
                    stat.AppName,
                    stat.TotalSeconds,
                    stat.SessionCount,
                    stat.LastPlayedAtUtc,
                    installedEntry is not null,
                    installedEntry?.AvailableToCurrentUser == true,
                    installedEntry?.AvailabilityMessage);
            })
            .ToArray();

        return new DashboardDataSnapshot(
            recent.Sum(item => item.TotalSeconds),
            recent.Sum(item => item.SessionCount),
            recent.Length,
            recent.FirstOrDefault(item => item.CanLaunch),
            recent);
    }

    public async Task<InstalledAppEntry?> GetLaunchEntryAsync(
        string? grevId,
        string appId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(grevId) || string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        var installed = await _installedApps.GetInstalledForUserAsync(grevId, cancellationToken);
        return installed.FirstOrDefault(entry =>
            entry.AvailableToCurrentUser &&
            string.Equals(entry.Manifest.Definition.AppId, appId, StringComparison.OrdinalIgnoreCase));
    }
}
