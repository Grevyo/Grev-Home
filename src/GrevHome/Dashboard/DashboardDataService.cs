using GrevHome.Apps;
using GrevHome.Games;
using GrevHome.Runtime;
using GrevHome.Storage;
using GrevHome.Store;

namespace GrevHome.Dashboard;

public sealed record DashboardAppActivity(
    string AppId,
    string AppName,
    long TotalSeconds,
    int SessionCount,
    DateTimeOffset LastPlayedAtUtc,
    bool IsInstalled,
    bool CanLaunch,
    string? AvailabilityMessage,
    ResolvedAppPresentation? Presentation);

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
/// Builds account-facing dashboard state from Grev Home's existing sources of truth. Completed
/// runtime sessions stay in PlaytimeService; launch availability is resolved against either the
/// installed app catalogue or the owning GrevID's individual-game library. No second activity
/// database is created merely to make emulated games appear in Continue/Recent.
/// </summary>
public sealed class DashboardDataService
{
    private readonly PlaytimeService _playtime;
    private readonly InstalledAppService _installedApps;
    private readonly GameLibraryService _games;
    private readonly GameLaunchResolver _gameLaunchResolver = new();
    private readonly GrevStoreCatalogService _storeCatalog = new();
    private readonly AppPresentationService _presentation;

    public DashboardDataService(AppPaths paths, InstalledAppService installedApps)
    {
        _playtime = new PlaytimeService(paths);
        _installedApps = installedApps;
        _games = new GameLibraryService(paths);
        _presentation = new AppPresentationService(paths);
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
        var games = await _games.GetForProfileAsync(grevId, cancellationToken);
        var installedByAppId = installed
            .GroupBy(entry => entry.Manifest.Definition.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(entry => entry.AvailableToCurrentUser).First(),
                StringComparer.OrdinalIgnoreCase);
        var gamesById = games.ToDictionary(game => game.GameId, StringComparer.OrdinalIgnoreCase);

        var recent = playtime.Apps.Values
            .OrderByDescending(stat => stat.LastPlayedAtUtc)
            .ThenBy(stat => stat.AppName, StringComparer.OrdinalIgnoreCase)
            .Select(async stat =>
            {
                if (installedByAppId.TryGetValue(stat.AppId, out var installedEntry))
                {
                    var package = _storeCatalog.Find(stat.AppId);
                    var presentation = package is null
                        ? null
                        : await _presentation.ResolveAsync(grevId, package, cancellationToken);
                    return new DashboardAppActivity(
                        stat.AppId,
                        presentation?.DisplayName ?? stat.AppName,
                        stat.TotalSeconds,
                        stat.SessionCount,
                        stat.LastPlayedAtUtc,
                        true,
                        installedEntry.AvailableToCurrentUser,
                        installedEntry.AvailabilityMessage,
                        presentation);
                }

                if (gamesById.TryGetValue(stat.AppId, out var game))
                {
                    var (canLaunch, message) = ResolveGameAvailability(game, installed, grevId);
                    var presentation = new ResolvedAppPresentation(
                        game.DisplayName,
                        "#0F2F6E",
                        game.IconPath,
                        game.TileMediaPath,
                        null,
                        !string.IsNullOrWhiteSpace(game.IconPath) || !string.IsNullOrWhiteSpace(game.TileMediaPath));
                    return new DashboardAppActivity(
                        stat.AppId,
                        game.DisplayName,
                        stat.TotalSeconds,
                        stat.SessionCount,
                        stat.LastPlayedAtUtc,
                        true,
                        canLaunch,
                        message,
                        presentation);
                }

                return new DashboardAppActivity(
                    stat.AppId,
                    stat.AppName,
                    stat.TotalSeconds,
                    stat.SessionCount,
                    stat.LastPlayedAtUtc,
                    false,
                    false,
                    null,
                    null);
            })
            .ToArray();

        var resolvedRecent = await Task.WhenAll(recent);

        return new DashboardDataSnapshot(
            resolvedRecent.Sum(item => item.TotalSeconds),
            resolvedRecent.Sum(item => item.SessionCount),
            resolvedRecent.Length,
            resolvedRecent.FirstOrDefault(item => item.CanLaunch),
            resolvedRecent);
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
        var app = installed.FirstOrDefault(entry =>
            entry.AvailableToCurrentUser &&
            string.Equals(entry.Manifest.Definition.AppId, appId, StringComparison.OrdinalIgnoreCase));
        if (app is not null)
        {
            return app;
        }

        if (!appId.StartsWith("game.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var games = await _games.GetForProfileAsync(grevId, cancellationToken);
        var game = games.FirstOrDefault(candidate =>
            string.Equals(candidate.GameId, appId, StringComparison.OrdinalIgnoreCase));
        return game is null
            ? null
            : _gameLaunchResolver.Resolve(game, installed, grevId);
    }

    private (bool CanLaunch, string? Message) ResolveGameAvailability(
        GameLibraryEntry game,
        IReadOnlyList<InstalledAppEntry> installed,
        string grevId)
    {
        try
        {
            _gameLaunchResolver.Resolve(game, installed, grevId);
            return (true, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (false, ex.Message);
        }
    }
}
