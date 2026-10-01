using System.Text.Json;

namespace GrevHome.Profiles;

/// <summary>One row in a list widget: a game, a friend, an achievement.</summary>
public sealed record ProfileWidgetLine(
    string Title,
    string? Detail = null,
    string? ImageUrl = null,
    string? UserId = null,
    string? Availability = null,
    bool IsPerson = false);

public sealed record ProfileWidgetStat(string Label, string Value);

/// <summary>
/// What a widget tile shows, independent of where the data came from (grev.dad's resolved widget
/// or this PC's own history when offline). ProfileTileBoard draws it.
/// </summary>
public sealed record ProfileWidgetView(
    ProfileWidgetKind Kind,
    IReadOnlyList<ProfileWidgetLine> Lines,
    IReadOnlyList<ProfileWidgetStat> Stats,
    string? Highlight = null,
    string? Subtitle = null,
    string? Body = null,
    string? EmptyText = null)
{
    public static ProfileWidgetView Message(ProfileWidgetKind kind, string text) => new(kind, [], [], EmptyText: text);
}

/// <summary>
/// Builds <see cref="ProfileWidgetView"/>s. FromServer reads the JSON grev.dad's resolveWidgets
/// (src/profile-unified.ts) returns for one tile; FromLocal makes the same views from local data
/// for a profile that is not linked or is offline. Pure, so tests run without WPF.
/// </summary>
public static class ProfileWidgetViews
{
    public static ProfileWidgetView FromServer(ProfileWidgetKind kind, JsonElement data, int count, bool isSelf)
    {
        if (data.ValueKind != JsonValueKind.Object) return ProfileWidgetView.Message(kind, "Loading…");
        if (Bool(data, "hidden")) return ProfileWidgetView.Message(kind, "Shared with friends only.");

        switch (kind)
        {
            case ProfileWidgetKind.RecentGames:
            case ProfileWidgetKind.MostPlayed:
            {
                var lines = Items(data, "items").Take(count).Select(item => new ProfileWidgetLine(
                    String(item, "title") ?? "Game",
                    kind == ProfileWidgetKind.MostPlayed
                        ? $"{FormatDuration(Long(item, "totalSeconds"))} • {Plural(Long(item, "sessions"), "session")}"
                        : JoinDetail(
                            String(item, "title") != String(item, "appName") ? String(item, "appName") : null,
                            FormatAgo(Long(item, "lastPlayedAt"))))).ToArray();
                return new(kind, lines, [], EmptyText: lines.Length == 0 ? "No games played in Grev Home yet." : null);
            }
            case ProfileWidgetKind.GameActivity:
            {
                var now = data.TryGetProperty("nowPlaying", out var playing) && playing.ValueKind == JsonValueKind.Object
                    ? String(playing, "title")
                    : null;
                var lines = Items(data, "sessions").Take(count).Select(item => new ProfileWidgetLine(
                    String(item, "title") ?? "Game",
                    $"{FormatDuration(Long(item, "durationSeconds"))} • {FormatAgo(Long(item, "endedAt"))}")).ToArray();
                return new(kind, lines, [], Highlight: now is null ? null : $"Now playing {now}",
                    EmptyText: lines.Length == 0 && now is null ? "No recent activity." : null);
            }
            case ProfileWidgetKind.FavouriteGames:
            {
                var lines = Items(data, "items").Take(count)
                    .Select(item => new ProfileWidgetLine(String(item, "title") ?? "Game", Blank(String(item, "platform")))).ToArray();
                return new(kind, lines, [], EmptyText: lines.Length == 0
                    ? (isSelf ? "Star games in your library to show them here." : "No favourites yet.")
                    : null);
            }
            case ProfileWidgetKind.BestFriends:
            {
                var lines = Items(data, "items").Take(count).Select(item => new ProfileWidgetLine(
                    String(item, "displayName") ?? "Friend",
                    Blank(String(item, "activityText")),
                    String(item, "avatarMedia"),
                    String(item, "userId"),
                    String(item, "availability"),
                    IsPerson: true)).ToArray();
                return new(kind, lines, [], EmptyText: lines.Length == 0 ? "No best friends picked yet." : null);
            }
            case ProfileWidgetKind.Bio:
            {
                var headline = Blank(String(data, "headline"));
                var bio = Blank(String(data, "bio"));
                return new(kind, [], [], Subtitle: headline, Body: bio, EmptyText: headline is null && bio is null ? "No bio yet." : null);
            }
            case ProfileWidgetKind.Stats:
            {
                var stats = new List<ProfileWidgetStat>();
                AddStat(stats, "Level", data, "level", value => value.ToString("N0"));
                AddStat(stats, "XP", data, "totalXp", value => value.ToString("N0"));
                AddStat(stats, "Played", data, "totalTrackedSeconds", FormatDuration);
                AddStat(stats, "Sessions", data, "completedSessions", value => value.ToString("N0"));
                AddStat(stats, "Games", data, "uniqueApps", value => value.ToString("N0"));
                AddStat(stats, "Achievements", data, "achievements", value => value.ToString("N0"));
                return new(kind, [], stats);
            }
            case ProfileWidgetKind.Achievements:
            {
                var lines = Items(data, "items").Take(count).Select(item => new ProfileWidgetLine(
                    String(item, "name") ?? "Achievement", Blank(String(item, "description")), AbsoluteUrl(String(item, "imageUrl")))).ToArray();
                return new(kind, lines, [], Subtitle: $"{Long(data, "earned"):N0} of {Long(data, "available"):N0} earned",
                    EmptyText: lines.Length == 0 ? "No achievements yet." : null);
            }
            case ProfileWidgetKind.RetroAchievements:
            {
                if (!Bool(data, "linked"))
                    return ProfileWidgetView.Message(kind, isSelf
                        ? "Link your RetroAchievements username in Edit Profile."
                        : "No RetroAchievements account linked.");
                if (!data.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
                    return ProfileWidgetView.Message(kind, Blank(String(data, "error")) ?? $"Linked as {String(data, "username")}. Waiting for RetroAchievements.");
                var achievements = Items(summary, "recentAchievements").Take(count).Select(item => new ProfileWidgetLine(
                    String(item, "title") ?? "Achievement",
                    $"{String(item, "gameTitle")} • {Long(item, "points")} pts{(Bool(item, "hardcore") ? " • hardcore" : "")}",
                    String(item, "badgeUrl"))).ToArray();
                var lines = achievements.Length > 0
                    ? achievements
                    : Items(summary, "recentlyPlayed").Take(count).Select(item => new ProfileWidgetLine(
                        String(item, "title") ?? "Game",
                        $"{String(item, "console")} • {Long(item, "achieved")}/{Long(item, "total")}",
                        String(item, "iconUrl"))).ToArray();
                var rank = Long(summary, "rank");
                return new(kind, lines, [],
                    Highlight: Blank(String(summary, "richPresence")),
                    Subtitle: $"{String(summary, "username")} • {Long(summary, "totalPoints"):N0} points{(rank > 0 ? $" • rank {rank:N0}" : "")}");
            }
            default:
                return ProfileWidgetView.Message(kind, "This widget needs a newer Grev Home.");
        }
    }

    /// <summary>The same widgets from this PC's own data, for an unlinked or offline profile.</summary>
    public static ProfileWidgetView FromLocal(
        ProfileWidgetKind kind,
        int count,
        ProfileStatsSnapshot? stats,
        string? bio,
        IReadOnlyList<FavouriteGame> favourites)
    {
        switch (kind)
        {
            case ProfileWidgetKind.RecentGames when stats is not null:
            {
                var lines = stats.RecentActivity.Take(count).Select(activity => new ProfileWidgetLine(
                    activity.AppName,
                    activity.IsRunning ? "Playing now" : FormatAgo(activity.LastActivityAtUtc.ToUnixTimeSeconds()))).ToArray();
                return new(kind, lines, [], EmptyText: lines.Length == 0 ? "No games played yet." : null);
            }
            case ProfileWidgetKind.MostPlayed when stats is not null:
            {
                var lines = stats.TopApps.Take(count).Select(app => new ProfileWidgetLine(
                    app.AppName, $"{FormatDuration(app.TotalSeconds)} • {Plural(app.SessionCount, "session")}")).ToArray();
                return new(kind, lines, [], EmptyText: lines.Length == 0 ? "No games played yet." : null);
            }
            case ProfileWidgetKind.GameActivity when stats is not null:
            {
                var running = stats.RecentActivity.FirstOrDefault(activity => activity.IsRunning);
                var lines = stats.RecentActivity.Where(activity => !activity.IsRunning).Take(count).Select(activity => new ProfileWidgetLine(
                    activity.AppName, $"{FormatDuration(activity.TotalSeconds)} total • {FormatAgo(activity.LastActivityAtUtc.ToUnixTimeSeconds())}")).ToArray();
                return new(kind, lines, [], Highlight: running is null ? null : $"Now playing {running.AppName}",
                    EmptyText: lines.Length == 0 && running is null ? "No recent activity." : null);
            }
            case ProfileWidgetKind.Stats when stats is not null:
                return new(kind, [],
                [
                    new("Level", stats.Progression.Level.ToString("N0")),
                    new("XP", stats.Progression.TotalXp.ToString("N0")),
                    new("Played", FormatDuration(stats.TotalTrackedSeconds)),
                    new("Sessions", stats.CompletedSessions.ToString("N0")),
                    new("Games", stats.UniqueApps.ToString("N0"))
                ]);
            case ProfileWidgetKind.Achievements when stats is not null:
            {
                var earned = stats.Milestones.Where(milestone => milestone.IsEarned).ToArray();
                var lines = earned.Take(count).Select(milestone => new ProfileWidgetLine(milestone.Title, milestone.Description)).ToArray();
                return new(kind, lines, [], Subtitle: $"{earned.Length} of {stats.Milestones.Count} earned",
                    EmptyText: lines.Length == 0 ? "No achievements yet." : null);
            }
            case ProfileWidgetKind.FavouriteGames:
            {
                var lines = favourites.Take(count).Select(game => new ProfileWidgetLine(game.Title, Blank(game.Platform))).ToArray();
                return new(kind, lines, [], EmptyText: lines.Length == 0 ? "Star games in your library to show them here." : null);
            }
            case ProfileWidgetKind.Bio:
                return new(kind, [], [], Body: Blank(bio), EmptyText: string.IsNullOrWhiteSpace(bio) ? "No bio yet." : null);
            case ProfileWidgetKind.BestFriends:
                return ProfileWidgetView.Message(kind, "Link Grev.dad to pick best friends.");
            case ProfileWidgetKind.RetroAchievements:
                return ProfileWidgetView.Message(kind, "Link Grev.dad to show RetroAchievements.");
            default:
                return ProfileWidgetView.Message(kind, "Loading…");
        }
    }

    public static string FormatDuration(long seconds)
    {
        seconds = Math.Max(0, seconds);
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    public static string FormatAgo(long epochSeconds, DateTimeOffset? now = null)
    {
        if (epochSeconds <= 0) return string.Empty;
        var age = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() - epochSeconds;
        if (age < 90) return "just now";
        if (age < 3600) return $"{age / 60} min ago";
        if (age < 86400) return $"{age / 3600} h ago";
        return $"{age / 86400} d ago";
    }

    private static string Plural(long count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";

    private static string? JoinDetail(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return present.Length == 0 ? null : string.Join(" • ", present);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // grev.dad's own achievement badges are site-relative paths.
    private static string? AbsoluteUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.StartsWith('/') ? $"https://grev.dad{value}"
        : value;

    private static IEnumerable<JsonElement> Items(JsonElement data, string name) =>
        data.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static string? String(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long Long(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static bool Bool(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void AddStat(List<ProfileWidgetStat> stats, string label, JsonElement data, string name, Func<long, string> format)
    {
        if (data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            stats.Add(new(label, format(number)));
    }
}
