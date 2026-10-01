using System.IO;
using System.Net.Http;
using GrevHome.Navigation;
using GrevHome.Profiles;

namespace GrevHome.Online;

/// <summary>
/// One profile across Grev Home and grev.dad: the profile page's tile grid with live widgets, a
/// friend's full profile, identity sync, favourite games, best friends and RetroAchievements.
/// A linked profile shows grev.dad's document (the same tiles and widgets as the website); an
/// unlinked or offline one shows its local tiles with widgets filled from this PC's history.
/// </summary>
public sealed partial class GrevDadCoordinator
{
    private GrevDadIdentitySyncService? _identitySync;
    private FavouriteGameService? _favouriteGames;
    private readonly Dictionary<string, GrevDadProfileDocument> _profileDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _bestFriendIds = new(StringComparer.OrdinalIgnoreCase);
    private string? _friendProfileUserId;
    private bool _friendProfileWired;

    /// <summary>Raised when a sync changed this PC's copy of a profile's identity (name, bio,
    /// avatar or banner came over from grev.dad), so the shell can reload that profile.</summary>
    public event Action<string>? LocalIdentityChanged;

    private GrevDadIdentitySyncService IdentitySync => _identitySync ??= new GrevDadIdentitySyncService(_paths, _profileService);
    public FavouriteGameService FavouriteGames => _favouriteGames ??= new FavouriteGameService(_paths);

    private GrevDadConnectionState ConnectionState(string grevId) =>
        _grevDadAccounts?.GetLastSnapshot(grevId).State ?? GrevDadConnectionState.Unlinked;

    private bool HasGrevDadLink(string grevId) =>
        ConnectionState(grevId) is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline;

    /// <summary>The last grev.dad profile document seen for this GrevID, if any (used for the card
    /// slot position while offline and by the tile editor).</summary>
    public GrevDadProfileDocument? GetCachedProfileDocument(string grevId) =>
        _profileDocuments.TryGetValue(grevId, out var document) ? document : null;

    /// <summary>Profile page tiles. Never throws: a failed sync falls back to local data.</summary>
    public async Task<ProfileSpace> LoadProfileSpaceAsync(LocalProfile profile, ProfileStatsSnapshot? stats)
    {
        var grevId = profile.GrevId;
        if (!profile.IsBuiltInGuest && ConnectionState(grevId) == GrevDadConnectionState.Linked && _grevDadAccounts is { } accounts)
        {
            try
            {
                await SyncProfileTilesNowAsync(grevId);
                await TrySyncFavouritesAsync(grevId);
                var before = (profile.DisplayName, profile.Bio, profile.AvatarImageFile);
                var document = await IdentitySync.SyncAsync(accounts, grevId) ?? await accounts.GetProfileDocumentAsync(grevId);
                _profileDocuments[grevId] = document;
                var after = (await _profileService.GetProfilesAsync())
                    .FirstOrDefault(item => string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
                if (after is not null && (after.DisplayName, after.Bio, after.AvatarImageFile) != before)
                {
                    LocalIdentityChanged?.Invoke(grevId);
                }
                return FromDocument(document, "Synced with your grev.dad profile.");
            }
            catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                       ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                // Fall through to the local view; the next visit retries.
            }
        }
        return await LoadLocalProfileSpaceAsync(profile, stats);
    }

    private async Task<ProfileSpace> LoadLocalProfileSpaceAsync(LocalProfile profile, ProfileStatsSnapshot? stats)
    {
        var tileService = new ProfileTileService(_paths);
        IReadOnlyList<ProfileTile> tiles;
        try { tiles = (await tileService.GetAsync(profile.GrevId)).Tiles; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { tiles = []; }
        var favourites = (await FavouriteGames.GetAsync(profile.GrevId)).Games;
        var widgets = tiles
            .Where(tile => tile.Widget is not null)
            .ToDictionary(
                tile => tile.TileId,
                tile => ProfileWidgetViews.FromLocal(tile.Widget!.Value, ProfileWidgets.EffectiveCount(tile.Widget.Value, tile.WidgetCount), stats, profile.Bio, favourites),
                StringComparer.OrdinalIgnoreCase);
        var cached = GetCachedProfileDocument(profile.GrevId);
        var avatarFile = string.IsNullOrWhiteSpace(profile.AvatarImageFile)
            ? null
            : Path.Combine(_paths.GetProfileRoot(profile.GrevId), Path.GetFileName(profile.AvatarImageFile));
        var card = new ProfileMiniCard(
            profile.DisplayName,
            profile.Username,
            cached?.Card.Headline,
            profile.Bio,
            AvatarFile: avatarFile,
            Availability: "online",
            Level: stats?.Progression.Level);
        var source = HasGrevDadLink(profile.GrevId)
            ? "Showing this PC's copy while grev.dad is unavailable."
            : "Local tiles. Link Grev.dad to share them on your grev.dad profile and with friends.";
        return new ProfileSpace(tiles, widgets, new Dictionary<string, string>(), tileService.GetMediaRoot(profile.GrevId), card,
            cached?.Preferences?.CardX ?? 0, cached?.Preferences?.CardY ?? 0, source);
    }

    private static ProfileSpace FromDocument(GrevDadProfileDocument document, string source)
    {
        var tiles = document.ToTiles();
        var widgets = tiles
            .Select(tile => (tile.TileId, View: document.WidgetView(tile)))
            .Where(item => item.View is not null)
            .ToDictionary(item => item.TileId, item => item.View!, StringComparer.OrdinalIgnoreCase);
        var presence = document.Presence;
        var card = new ProfileMiniCard(
            document.Card.DisplayName,
            document.Username,
            document.Card.Headline,
            document.Card.Bio,
            AvatarDataUrl: document.Card.AvatarMedia,
            Availability: presence?.Availability ?? "offline",
            ActivityText: presence?.ActivityType == "playing" && !string.IsNullOrWhiteSpace(presence.ActivityText) ? presence.ActivityText : null,
            Level: document.PublicCard?.ShowLevel == false && !document.IsSelf ? null : document.Level,
            IsVerified: document.IsVerified == true,
            IsBestFriend: document.IsBestFriend,
            BackgroundPrimary: document.Card.BackgroundPrimary,
            BackgroundSecondary: document.Card.BackgroundSecondary,
            BackgroundAngle: document.Card.BackgroundAngle,
            TextColour: document.Card.TextColour,
            BorderColour: document.Card.BorderColour);
        return new ProfileSpace(tiles, widgets, document.TileMedia, null, card,
            document.Preferences?.CardX ?? 0, document.Preferences?.CardY ?? 0, source);
    }

    /// <summary>Runs identity sync in the background (after linking, signing in, or editing the
    /// profile). Failures are left for the next sync.</summary>
    public async Task SyncIdentityQuietlyAsync(string grevId)
    {
        if (_grevDadAccounts is not { } accounts || ConnectionState(grevId) != GrevDadConnectionState.Linked) return;
        try
        {
            var before = (await _profileService.GetProfilesAsync()).FirstOrDefault(item => string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
            var document = await IdentitySync.SyncAsync(accounts, grevId);
            if (document is not null) _profileDocuments[grevId] = document;
            var after = (await _profileService.GetProfilesAsync()).FirstOrDefault(item => string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
            if (before is not null && after is not null &&
                (after.DisplayName, after.Bio, after.AvatarImageFile) != (before.DisplayName, before.Bio, before.AvatarImageFile))
            {
                LocalIdentityChanged?.Invoke(grevId);
            }
            await TrySyncFavouritesAsync(grevId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                   ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
        }
    }

    // --- favourites ----------------------------------------------------------------------------

    /// <summary>Stars or unstars a game. Works offline; a linked profile's change reaches grev.dad
    /// now or on the next sync. Returns whether the game is now a favourite.</summary>
    public async Task<bool> ToggleFavouriteGameAsync(string grevId, FavouriteGame game)
    {
        var linked = HasGrevDadLink(grevId);
        var isFavourite = await FavouriteGames.ToggleAsync(grevId, game, queueForSync: linked);
        if (linked) _ = TrySyncFavouritesAsync(grevId);
        return isFavourite;
    }

    private async Task TrySyncFavouritesAsync(string grevId)
    {
        if (_grevDadAccounts is not { } accounts || ConnectionState(grevId) != GrevDadConnectionState.Linked) return;
        try
        {
            var state = await FavouriteGames.GetAsync(grevId);
            foreach (var change in state.Pending)
            {
                if (change.Kind == FavouriteChangeKind.Add) await accounts.AddFavouriteAsync(grevId, change.Game);
                else await accounts.RemoveFavouriteAsync(grevId, change.Game.ItemKey);
            }
            var remote = await accounts.GetFavouritesAsync(grevId);
            await FavouriteGames.ApplyRemoteAsync(grevId, remote, state.Pending);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                   ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            // Pending changes stay queued.
        }
    }

    // --- best friends ---------------------------------------------------------------------------

    public bool IsBestFriend(string userId) => _bestFriendIds.Contains(userId);
    public IReadOnlySet<string> BestFriendIds => _bestFriendIds;

    private async Task RefreshBestFriendsAsync(string grevId)
    {
        if (_grevDadAccounts is not { } accounts || ConnectionState(grevId) != GrevDadConnectionState.Linked) return;
        try
        {
            var best = await accounts.GetBestFriendsAsync(grevId);
            _bestFriendIds.Clear();
            foreach (var friend in best.Items) _bestFriendIds.Add(friend.UserId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is InvalidOperationException or InvalidDataException)
        {
        }
    }

    private async Task ToggleBestFriendAsync(string friendUserId)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null || _grevDadAccounts is not { } accounts) return;
        var adding = !_bestFriendIds.Contains(friendUserId);
        try
        {
            var best = adding
                ? await accounts.AddBestFriendAsync(grevId, friendUserId)
                : await accounts.RemoveBestFriendAsync(grevId, friendUserId);
            _bestFriendIds.Clear();
            foreach (var friend in best.Items) _bestFriendIds.Add(friend.UserId);
            if (_friendProfileUserId == friendUserId)
            {
                _friendProfileView.SetBestFriend(_bestFriendIds.Contains(friendUserId), canChange: true);
                _friendProfileView.ShowStatus(adding ? "Added to your best friends." : "Removed from your best friends.");
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is InvalidOperationException or InvalidDataException)
        {
            _friendProfileView.ShowStatus(ex is InvalidOperationException ? ex.Message : "Best friends could not be updated. Check your connection.");
        }
    }

    // --- a friend's full profile ----------------------------------------------------------------

    private void WireFriendProfile()
    {
        if (_friendProfileWired) return;
        _friendProfileWired = true;
        _profileEditView.LinkRetroAchievementsRequested += username => _ = ChangeRetroAchievementsAsync(username);
        _profileEditView.UnlinkRetroAchievementsRequested += (_, _) => _ = ChangeRetroAchievementsAsync(null);
        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileEdit) _ = LoadRetroAchievementsForEditorAsync();
        };
        _friendProfileView.BestFriendToggleRequested += (_, _) =>
        {
            if (_friendProfileUserId is { } userId) _ = ToggleBestFriendAsync(userId);
        };
        _friendProfileView.FriendProfileRequested += userId => _ = OpenFriendByIdAsync(userId);
        _friendProfileView.LinkRequested += url => OpenGrevDadWebsiteUrl(url);
        _profileView.FriendProfileRequested += userId => _ = OpenFriendByIdAsync(userId);
        _profileView.LinkRequested += url => OpenGrevDadWebsiteUrl(url);
    }

    private async Task LoadFriendProfileDocumentAsync(GrevDadFriend friend, bool isSelf)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        _friendProfileUserId = isSelf ? null : friend.UserId;
        _friendProfileView.SetBestFriend(!isSelf && _bestFriendIds.Contains(friend.UserId), canChange: !isSelf && ConnectionState(grevId ?? "") == GrevDadConnectionState.Linked);
        if (grevId is null || _grevDadAccounts is not { } accounts) return;

        if (isSelf)
        {
            if (GetCachedProfileDocument(grevId) is { } own) _friendProfileView.SetProfileSpace(FromDocument(own, ""));
            return;
        }
        if (ConnectionState(grevId) != GrevDadConnectionState.Linked)
        {
            _friendProfileView.SetProfileSpace(null, "Their tiles show when Grev.dad is connected.");
            return;
        }
        _friendProfileView.SetProfileSpace(null, "Loading their tiles…");
        try
        {
            var document = await accounts.GetProfileDocumentAsync(grevId, friend.UserId);
            if (_friendProfileUserId != friend.UserId || _navigation.Current != Route.FriendProfile) return;
            if (document.IsBestFriend) _bestFriendIds.Add(friend.UserId);
            _friendProfileView.SetBestFriend(_bestFriendIds.Contains(friend.UserId), canChange: true);
            _friendProfileView.SetProfileSpace(FromDocument(document, ""), document.Card.Headline);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is InvalidOperationException or InvalidDataException)
        {
            if (_friendProfileUserId == friend.UserId)
                _friendProfileView.SetProfileSpace(null, "Their tiles could not be loaded. Choose Refresh on Friends and try again.");
        }
    }

    /// <summary>Opens a profile from a Best friends widget. Friends open their full profile; anyone
    /// else (a friend's best friend you are not friends with) cannot be opened from Grev Home.</summary>
    private async Task OpenFriendByIdAsync(string userId)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null || _grevDadAccounts is not { } accounts) return;
        try
        {
            var friends = await accounts.GetFriendsAsync(grevId, allowCachedWhenOffline: true);
            var friend = friends.FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase));
            if (friend is not null)
            {
                OpenFriendProfile(friend);
                return;
            }
            var message = "You can open the full profiles of your own friends. Add them with their friend code first.";
            if (_navigation.Current == Route.FriendProfile) _friendProfileView.ShowStatus(message);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is InvalidOperationException or InvalidDataException)
        {
        }
    }

    /// <summary>A link tile: grev.dad pages open in the Grev.dad browser, other HTTPS pages in
    /// Grev Home's general web browser (both controller-driven).</summary>
    private void OpenGrevDadWebsiteUrl(string url)
    {
        if (_session.PrimaryUser?.GrevId is null) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            if (_navigation.Current == Route.FriendProfile) _friendProfileView.ShowStatus("Only secure (https) links can open in Grev Home.");
            return;
        }
        var home = RequireGrevDadAccountService().BaseUri;
        if (string.Equals(uri.Host, home.Host, StringComparison.OrdinalIgnoreCase))
        {
            OpenGrevDadWebsite(uri);
            return;
        }
        _generalWebBrowser = true;
        _grevDadWebTarget = uri;
        _grevDadWebRequestedOwner = null;
        _grevDadWebRequiresActiveOwner = true;
        _navigation.Navigate(Route.GrevDadWeb);
    }

    // --- RetroAchievements ------------------------------------------------------------------------

    /// <summary>The Edit Profile section shows only for the linked Primary User's own profile.</summary>
    private async Task LoadRetroAchievementsForEditorAsync()
    {
        _profileEditView.SetRetroAchievementsState(null);
        var target = _getProfileTarget();
        var primary = _session.PrimaryUser?.GrevId;
        if (target is null || primary is null || !string.Equals(target.GrevId, primary, StringComparison.OrdinalIgnoreCase) ||
            ConnectionState(primary) != GrevDadConnectionState.Linked) return;
        try
        {
            var state = await GetRetroAchievementsAsync(primary);
            if (_navigation.Current == Route.ProfileEdit && _session.PrimaryUser?.GrevId == primary) _profileEditView.SetRetroAchievementsState(state);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is InvalidOperationException or InvalidDataException)
        {
        }
    }

    private async Task ChangeRetroAchievementsAsync(string? username)
    {
        var primary = _session.PrimaryUser?.GrevId;
        if (primary is null) return;
        try
        {
            var state = username is null ? await UnlinkRetroAchievementsAsync(primary) : await LinkRetroAchievementsAsync(primary, username);
            _profileEditView.SetRetroAchievementsState(state, username is null ? "RetroAchievements unlinked." : null);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is InvalidOperationException or InvalidDataException)
        {
            var current = await GetRetroAchievementsAsync(primary).ContinueWith(task => task.IsCompletedSuccessfully ? task.Result : null);
            _profileEditView.SetRetroAchievementsState(current ?? new GrevDadRetroAchievements(false, true),
                ex is InvalidOperationException ? ex.Message : "RetroAchievements could not be updated. Check your connection.");
        }
    }

    public Task<GrevDadRetroAchievements> GetRetroAchievementsAsync(string grevId) =>
        RequireGrevDadAccountService().GetRetroAchievementsAsync(grevId);

    public Task<GrevDadRetroAchievements> LinkRetroAchievementsAsync(string grevId, string username) =>
        RequireGrevDadAccountService().LinkRetroAchievementsAsync(grevId, username);

    public Task<GrevDadRetroAchievements> UnlinkRetroAchievementsAsync(string grevId) =>
        RequireGrevDadAccountService().UnlinkRetroAchievementsAsync(grevId);
}
