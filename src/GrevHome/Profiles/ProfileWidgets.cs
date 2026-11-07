namespace GrevHome.Profiles;

/// <summary>
/// Live widget tiles: profile tiles that fill themselves with data instead of typed text. Mirrors
/// PROFILE_WIDGETS in grev.dad's src/profile-widgets.ts; a widget tile is a Text tile with a
/// widget kind, which is also how grev.dad stores it, so older clients still show it as a plain
/// titled tile.
/// </summary>
public enum ProfileWidgetKind
{
    RecentGames,
    GameActivity,
    MostPlayed,
    FavouriteGames,
    BestFriends,
    Bio,
    Stats,
    Achievements,
    RetroAchievements
}

public sealed record ProfileWidgetInfo(
    ProfileWidgetKind Kind,
    string WireName,
    string Label,
    string Hint,
    int Width,
    int Height,
    bool IsList,
    int DefaultCount);

public static class ProfileWidgets
{
    public const int MinCount = 1;
    public const int MaxCount = 12;

    public static IReadOnlyList<ProfileWidgetInfo> All { get; } =
    [
        new(ProfileWidgetKind.RecentGames, "recent-games", "Recent games", "What you played last, newest first.", 4, 2, true, 6),
        new(ProfileWidgetKind.GameActivity, "game-activity", "Game activity", "Now playing plus your latest sessions.", 4, 2, true, 5),
        new(ProfileWidgetKind.MostPlayed, "most-played", "Most played", "Your games ranked by play time.", 3, 2, true, 5),
        new(ProfileWidgetKind.FavouriteGames, "favourite-games", "Favourite games", "Games you starred in the library or on grev.dad.", 4, 2, true, 6),
        new(ProfileWidgetKind.BestFriends, "best-friends", "Best friends", "Up to twelve friends you pick.", 4, 2, true, 6),
        new(ProfileWidgetKind.Bio, "bio", "Bio", "Your headline and bio.", 3, 2, false, 0),
        new(ProfileWidgetKind.Stats, "stats", "Stats", "Level, XP, play time and sessions.", 3, 2, false, 0),
        new(ProfileWidgetKind.Achievements, "achievements", "Achievements", "Your latest achievements.", 3, 2, true, 6),
        new(ProfileWidgetKind.RetroAchievements, "retroachievements", "RetroAchievements", "Points, rank and recent unlocks from RetroAchievements.", 4, 3, true, 5)
    ];

    public static ProfileWidgetInfo Info(ProfileWidgetKind kind) => All.First(info => info.Kind == kind);

    public static string ToWire(ProfileWidgetKind kind) => Info(kind).WireName;

    /// <summary>Null for no widget and for a kind this build does not know; the tile then shows
    /// as a plain tile, as grev.dad does for an unknown stored widget.</summary>
    public static ProfileWidgetKind? FromWire(string? value) =>
        All.FirstOrDefault(info => string.Equals(info.WireName, value, StringComparison.OrdinalIgnoreCase))?.Kind;

    /// <summary>The number of items a list widget shows, defaulted and clamped like widgetCount in
    /// src/profile-widgets.ts.</summary>
    public static int EffectiveCount(ProfileWidgetKind kind, int? count)
    {
        var info = Info(kind);
        return Math.Clamp(count ?? info.DefaultCount, MinCount, MaxCount);
    }

    /// <summary>A new widget tile at the first free place, or null when the grid is full.</summary>
    public static ProfileTile? CreateTile(IReadOnlyList<ProfileTile> existing, ProfileWidgetKind kind)
    {
        if (existing.Count >= ProfileTileGrid.MaxTiles) return null;
        var info = Info(kind);
        var placement = ProfileTileGrid.FindFreePlacement(existing, ProfileTileKind.Text, info.Width, info.Height)
            ?? ProfileTileGrid.FindFreePlacement(existing, ProfileTileKind.Text, 2, 2);
        return placement is null
            ? null
            : placement with { TileId = Guid.NewGuid().ToString(), Title = info.Label, Widget = kind };
    }
}
