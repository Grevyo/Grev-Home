using System.Windows;
using GrevHome.Online;

namespace GrevHome.Views;

public partial class ProfileView
{
    private bool _cloudLinked;
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
