namespace GrevHome.Profiles;

/// <summary>
/// The mini profile: the small card shown in friends lists and in the profile grid's card slot.
/// Same fields as grev.dad's profile card so both look like the same person.
/// </summary>
public sealed record ProfileMiniCard(
    string DisplayName,
    string? Username = null,
    string? Headline = null,
    string? Bio = null,
    string? AvatarDataUrl = null,
    string? AvatarFile = null,
    string Availability = "offline",
    string? ActivityText = null,
    int? Level = null,
    bool IsVerified = false,
    bool IsBestFriend = false,
    string BackgroundPrimary = "#11161d",
    string BackgroundSecondary = "#3157c9",
    int BackgroundAngle = 135,
    string TextColour = "#f4f7fb",
    string BorderColour = "#526074");

/// <summary>
/// Everything ProfileTileBoard needs to draw a profile's tile grid: the tiles, each widget's data,
/// where tile pictures come from, and the mini card that sits in the card slot (4 × 6 cells at
/// CardX/CardY, exactly where grev.dad puts its profile card).
/// </summary>
public sealed record ProfileSpace(
    IReadOnlyList<ProfileTile> Tiles,
    IReadOnlyDictionary<string, ProfileWidgetView> Widgets,
    IReadOnlyDictionary<string, string> MediaDataUrls,
    string? LocalMediaRoot,
    ProfileMiniCard? Card,
    int CardX = 0,
    int CardY = 0,
    string Source = "")
{
    public const int CardColumns = 4;
    public const int CardRows = 6;

    /// <summary>The card slot is drawn only where no tile covers it (a layout made before the card
    /// slot existed may have tiles there).</summary>
    public bool ShowsCardSlot =>
        Card is not null &&
        !Tiles.Any(tile => ProfileTileGrid.Overlaps(tile, CardSlot));

    public ProfileTile CardSlot => new(Guid.Empty.ToString(), ProfileTileKind.Text, CardX, CardY, CardColumns, CardRows);
}
