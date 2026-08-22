using System.Windows;
using GrevHome.Online;

namespace GrevHome.Views;

public partial class ProfileView
{
    public void SetGrevDadState(GrevDadAccountSnapshot snapshot)
    {
        if (snapshot.Account is { } account &&
            snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
        {
            GrevDadLinkedText.Text = snapshot.State == GrevDadConnectionState.Offline
                ? $"Grev.dad • @{account.Username} • Offline"
                : $"Grev.dad • @{account.Username}";
            GrevDadLinkedText.Visibility = Visibility.Visible;
            return;
        }

        GrevDadLinkedText.Text = string.Empty;
        GrevDadLinkedText.Visibility = Visibility.Collapsed;
    }
}
