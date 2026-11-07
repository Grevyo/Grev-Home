using System.Net.Http;
using System.Text.Json;
using GrevHome.Profiles;

namespace GrevHome.Online;

// The shared grev.dad profile, as src/profile-unified.ts serves it to Grev Home.

public sealed record GrevDadProfileCard(
    string DisplayName,
    string? Headline = null,
    string? Bio = null,
    string? Location = null,
    string? WebsiteUrl = null,
    string? AvatarMedia = null,
    string? CoverMedia = null,
    string BackgroundPrimary = "#11161d",
    string BackgroundSecondary = "#3157c9",
    int BackgroundAngle = 135,
    string TextColour = "#f4f7fb",
    string BorderColour = "#526074");

public sealed record GrevDadProfilePreferences(
    string Density = "comfortable",
    int TileGap = 12,
    int OuterMargin = 0,
    int CardX = 0,
    int CardY = 0);

public sealed record GrevDadProfilePresenceWire(
    string Availability = "offline",
    string StatusText = "",
    string ActivityType = "none",
    string ActivityText = "",
    long? ExpiresAt = null,
    long? UpdatedAt = null);

public sealed record GrevDadProfileDocument(
    string Id,
    string? Username,
    string DisplayName,
    string Relationship,
    bool IsSelf,
    bool IsBestFriend,
    long TotalXp,
    int Level,
    GrevDadProfileCard Card,
    IReadOnlyList<GrevDadProfileTileWire> Tiles,
    GrevDadProfilePreferences? Preferences = null,
    GrevDadProfilePresenceWire? Presence = null,
    GrevDadPublicCard? PublicCard = null,
    Dictionary<string, JsonElement>? Widgets = null,
    bool? IsVerified = null,
    string? FriendCode = null,
    long TilesUpdatedAt = 0)
{
    /// <summary>Tiles for drawing (pictures stay as data URLs in <see cref="TileMedia"/>).</summary>
    public IReadOnlyList<ProfileTile> ToTiles() =>
        Tiles.Select(tile => GrevDadProfileSyncService.FromWireTile(tile, null)).ToArray();

    public IReadOnlyDictionary<string, string> TileMedia =>
        Tiles.Where(tile => !string.IsNullOrWhiteSpace(tile.BackgroundMedia))
            .ToDictionary(tile => tile.TileId, tile => tile.BackgroundMedia!, StringComparer.OrdinalIgnoreCase);

    /// <summary>The widget view for one tile, from the data grev.dad resolved for this viewer.</summary>
    public ProfileWidgetView? WidgetView(ProfileTile tile) =>
        tile.Widget is not { } kind ? null
        : Widgets is not null && Widgets.TryGetValue(tile.TileId, out var data)
            ? ProfileWidgetViews.FromServer(kind, data, ProfileWidgets.EffectiveCount(kind, tile.WidgetCount), IsSelf)
            : ProfileWidgetView.Message(kind, "Hidden by this member's privacy settings.");
}

public sealed record GrevDadBestFriend(string UserId, string Username, string DisplayName, string? AvatarMedia = null, string Availability = "offline");
public sealed record GrevDadFriendSummary(string UserId, string Username, string DisplayName);
public sealed record GrevDadBestFriends(IReadOnlyList<GrevDadBestFriend> Items, IReadOnlyList<GrevDadFriendSummary> Friends);

public sealed record GrevDadRetroAchievements(
    bool Linked,
    bool Configured,
    string? Username = null,
    string? Error = null,
    long? FetchedAt = null,
    JsonElement? Summary = null)
{
    public long TotalPoints =>
        Summary is { ValueKind: JsonValueKind.Object } summary &&
        summary.TryGetProperty("totalPoints", out var points) && points.TryGetInt64(out var value) ? value : 0;
}

internal sealed record ProfileDocumentApiResponse(bool Ok, string? Message, GrevDadProfileDocument? Profile);
internal sealed record FavouriteApiItem(string ItemKey, string Title, string? Platform, string? AppId, string? ContentId, long? AddedAt);
internal sealed record FavouritesApiResponse(bool Ok, string? Message, IReadOnlyList<FavouriteApiItem>? Items);
internal sealed record BestFriendsApiResponse(bool Ok, string? Message, IReadOnlyList<GrevDadBestFriend>? Items, IReadOnlyList<GrevDadFriendSummary>? Friends);
internal sealed record RetroAchievementsApiResponse(bool Ok, string? Message, GrevDadRetroAchievements? RetroAchievements);

public sealed partial class GrevDadAccountService
{
    /// <summary>This account's own profile document (userId null) or a friend's. A friend's
    /// profile is refused by grev.dad unless the two are friends and neither has blocked the other.</summary>
    public async Task<GrevDadProfileDocument> GetProfileDocumentAsync(
        string grevId, string? userId = null, CancellationToken cancellationToken = default)
    {
        var path = userId is null ? "api/grev-home/profile" : $"api/grev-home/profiles/{Uri.EscapeDataString(userId)}";
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Get, path, null, cancellationToken);
        var payload = await GrevDadNetworkSupport.ReadJsonAsync<ProfileDocumentApiResponse>(response, _json, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return payload.Profile ?? throw new InvalidDataException("Grev.dad returned no profile.");
    }

    /// <summary>Changes only the fields present in <paramref name="changes"/>. A null value clears
    /// that field (for example removes the avatar), so callers send exactly what changed.</summary>
    public async Task<GrevDadProfileDocument> UpdateIdentityAsync(
        string grevId, IReadOnlyDictionary<string, object?> changes, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Put, "api/grev-home/profile/identity", changes, cancellationToken);
        var payload = await GrevDadNetworkSupport.ReadJsonAsync<ProfileDocumentApiResponse>(response, _json, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return payload.Profile ?? throw new InvalidDataException("Grev.dad returned no profile.");
    }

    public async Task<IReadOnlyList<FavouriteGame>> GetFavouritesAsync(string grevId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Get, "api/grev-home/profile/favourites", null, cancellationToken);
        return await ReadFavouritesAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<FavouriteGame>> AddFavouriteAsync(string grevId, FavouriteGame game, CancellationToken cancellationToken = default)
    {
        var item = new Dictionary<string, object?>
        {
            ["itemKey"] = game.ItemKey, ["title"] = game.Title, ["platform"] = game.Platform,
            ["appId"] = game.AppId, ["contentId"] = game.ContentId
        };
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Post, "api/grev-home/profile/favourites", new { item }, cancellationToken);
        return await ReadFavouritesAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<FavouriteGame>> RemoveFavouriteAsync(string grevId, string itemKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(grevId, HttpMethod.Delete,
            $"api/grev-home/profile/favourites/{Uri.EscapeDataString(itemKey)}", null, cancellationToken);
        return await ReadFavouritesAsync(response, cancellationToken);
    }

    private async Task<IReadOnlyList<FavouriteGame>> ReadFavouritesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await GrevDadNetworkSupport.ReadJsonAsync<FavouritesApiResponse>(response, _json, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return (payload.Items ?? []).Select(item => new FavouriteGame(
            item.ItemKey, item.Title, item.Platform ?? "", item.AppId ?? "", item.ContentId ?? "",
            item.AddedAt is { } added ? DateTimeOffset.FromUnixTimeSeconds(added) : null)).ToArray();
    }

    public Task<GrevDadBestFriends> GetBestFriendsAsync(string grevId, CancellationToken cancellationToken = default) =>
        SendBestFriendsAsync(grevId, HttpMethod.Get, "api/grev-home/profile/best-friends", null, cancellationToken);

    public Task<GrevDadBestFriends> AddBestFriendAsync(string grevId, string userId, CancellationToken cancellationToken = default) =>
        SendBestFriendsAsync(grevId, HttpMethod.Post, "api/grev-home/profile/best-friends", new { userId }, cancellationToken);

    public Task<GrevDadBestFriends> RemoveBestFriendAsync(string grevId, string userId, CancellationToken cancellationToken = default) =>
        SendBestFriendsAsync(grevId, HttpMethod.Delete, $"api/grev-home/profile/best-friends/{Uri.EscapeDataString(userId)}", null, cancellationToken);

    private async Task<GrevDadBestFriends> SendBestFriendsAsync(
        string grevId, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(grevId, method, path, body, cancellationToken);
        var payload = await GrevDadNetworkSupport.ReadJsonAsync<BestFriendsApiResponse>(response, _json, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return new GrevDadBestFriends(payload.Items ?? [], payload.Friends ?? []);
    }

    public Task<GrevDadRetroAchievements> GetRetroAchievementsAsync(string grevId, CancellationToken cancellationToken = default) =>
        SendRetroAchievementsAsync(grevId, HttpMethod.Get, null, cancellationToken);

    /// <summary>Links a RetroAchievements username. grev.dad checks it exists and keeps its own API
    /// key; no RetroAchievements password or key is ever entered in Grev Home.</summary>
    public Task<GrevDadRetroAchievements> LinkRetroAchievementsAsync(string grevId, string username, CancellationToken cancellationToken = default) =>
        SendRetroAchievementsAsync(grevId, HttpMethod.Put, new { username }, cancellationToken);

    public Task<GrevDadRetroAchievements> UnlinkRetroAchievementsAsync(string grevId, CancellationToken cancellationToken = default) =>
        SendRetroAchievementsAsync(grevId, HttpMethod.Delete, null, cancellationToken);

    public Task<GrevDadRetroAchievements> RefreshRetroAchievementsAsync(string grevId, CancellationToken cancellationToken = default) =>
        SendRetroAchievementsAsync(grevId, HttpMethod.Post, new { }, cancellationToken);

    private async Task<GrevDadRetroAchievements> SendRetroAchievementsAsync(
        string grevId, HttpMethod method, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(grevId, method, "api/grev-home/profile/retroachievements", body, cancellationToken);
        var payload = await GrevDadNetworkSupport.ReadJsonAsync<RetroAchievementsApiResponse>(response, _json, cancellationToken);
        EnsureSuccessful(response, payload.Ok, payload.Message);
        return payload.RetroAchievements ?? new GrevDadRetroAchievements(false, false);
    }
}
