using System.Windows;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileView
{
    private bool _cloudLinked;

    /// <summary>Edit Tiles on the profile page (opens the controller tile editor).</summary>
    public event EventHandler? EditTilesRequested;
    public event Action<string>? FriendProfileRequested;
    public event Action<string>? LinkRequested;

    private bool _tileBoardWired;

    /// <summary>Shows the profile's tile grid with live widgets. null clears it.</summary>
    public void SetProfileSpace(ProfileSpace? space, string? status = null)
    {
        if (!_tileBoardWired)
        {
            _tileBoardWired = true;
            TileBoard.FriendRequested += userId => FriendProfileRequested?.Invoke(userId);
            TileBoard.LinkRequested += url => LinkRequested?.Invoke(url);
        }
        TileBoard.SetSpace(space);
        if (status is not null) ProfileSpaceStatusText.Text = status;
    }

    private void EditTiles_Click(object sender, RoutedEventArgs e) => EditTilesRequested?.Invoke(this, EventArgs.Empty);
    public void SetCloudAccountData(GrevDadAccountData? data, bool pending)
    {
        CloudAccountText.Visibility = data is null && !_cloudLinked ? Visibility.Collapsed : Visibility.Visible;
        if (data is null)
        {
            CloudAccountText.Text = "Waiting for the first cloud sync. Local data is not yet confirmed in the cloud.";
            return;
        }
        var first = data.Sources.Where(s=>s.ProfileCreatedAt.HasValue).Select(s=>s.ProfileCreatedAt!.Value).DefaultIfEmpty().Min();
        CloudAccountText.Text = $"{(pending ? "Waiting to sync" : "Cloud account data")} • Last synced {DateTimeOffset.FromUnixTimeSeconds(data.DownloadedAt).ToLocalTime():g}\n" +
            $"Account created {DateTimeOffset.FromUnixTimeSeconds(data.AccountCreatedAt).ToLocalTime():d MMM yyyy}" +
            (first > 0 ? $" • First Grev Home profile {DateTimeOffset.FromUnixTimeSeconds(first).ToLocalTime():d MMM yyyy}" : "");
    }

    public void SetGrevDadState(GrevDadAccountSnapshot snapshot)
    {
        // View Profile is display-only for Grev.dad. Linking, unlinking, approval and privacy
        // are profile-edit features; the normal profile surface only identifies the linked account.
        if (snapshot.Account is { } account &&
            snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
        {
            _cloudLinked = true;
            if (CloudAccountText.Visibility != Visibility.Visible) SetCloudAccountData(null,true);
            GrevDadLinkedText.Text = $"Grev.dad • @{account.Username}";
            GrevDadLinkedText.Visibility = Visibility.Visible;
            return;
        }

        GrevDadLinkedText.Text = string.Empty;
        _cloudLinked = false;
        SetCloudAccountData(null,false);
        GrevDadLinkedText.Visibility = Visibility.Collapsed;
    }
}
