using System.Windows;
using GrevHome.Online;

namespace GrevHome.Views;

public partial class ProfileView
{
    public void SetGrevDadState(GrevDadAccountSnapshot snapshot)
    {
        // View Profile is display-only for Grev.dad. Linking, unlinking, approval and privacy
        // are profile-edit features; the normal profile surface only identifies the linked account.
        if (snapshot.Account is { } account &&
            snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
        {
            GrevDadLinkedText.Text = $"Grev.dad • @{account.Username}";
            GrevDadLinkedText.Visibility = Visibility.Visible;
            return;
        }

        GrevDadLinkedText.Text = string.Empty;
        GrevDadLinkedText.Visibility = Visibility.Collapsed;
    }
}
