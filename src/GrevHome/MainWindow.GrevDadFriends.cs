using System.IO;
using System.Windows;
using GrevHome.Navigation;
using GrevHome.Online;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly FriendsView _friendsView = new();
    private bool _grevDadFriendsReady;

    private void InitializeGrevDadFriendsIntegration()
    {
        if (_grevDadFriendsReady) return;
        _grevDadFriendsReady = true;
        _dashboardView.FriendsRequested += (_, _) => OpenFriends();
        _friendsView.BackRequested += (_, _) => _navigation.GoBack();
        _friendsView.RefreshRequested += (_, _) => _ = RefreshFriendsSurfacesAsync(forceLoad: true);
        _navigation.RouteChanged += route =>
        {
            if (route == Route.Friends)
            {
                RouteHost.Content = _friendsView;
                _ = RefreshFriendsSurfacesAsync(forceLoad: true);
            }
        };
        RequireGrevDadAccountService().SnapshotChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshFriendsSurfacesAsync(forceLoad: false)));
        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = RefreshFriendsSurfacesAsync(forceLoad: true)));
        _ = RefreshFriendsSurfacesAsync(forceLoad: true);
    }

    private void OpenFriends()
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null) return;
        var state = RequireGrevDadAccountService().GetLastSnapshot(grevId).State;
        if (state is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
            _navigation.Navigate(Route.Friends);
    }

    private async Task RefreshFriendsSurfacesAsync(bool forceLoad)
    {
        var service = _grevDadAccounts;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null)
        {
            SetFriendsUnavailable();
            return;
        }

        try
        {
            var snapshot = forceLoad ? await service.LoadLocalStateAsync(primary.GrevId) : service.GetLastSnapshot(primary.GrevId);
            var available = snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline;
            if (!available) { SetFriendsUnavailable(); return; }
            var friends = await service.GetFriendsAsync(primary.GrevId, allowCachedWhenOffline: true);
            var offline = snapshot.State == GrevDadConnectionState.Offline;
            ShellFriendsButton.Visibility = Visibility.Visible;
            _dashboardView.SetFriends(true, friends, offline);
            _friendsView.SetFriends(snapshot.Account?.DisplayName ?? primary.DisplayName, friends, offline);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var snapshot = service.GetLastSnapshot(primary.GrevId);
            var available = snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline;
            ShellFriendsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            _dashboardView.SetFriends(available, Array.Empty<GrevDadFriend>(), offline: true);
            if (_navigation.Current == Route.Friends) _friendsView.ShowStatus($"Friends could not be refreshed: {ex.Message}");
        }
    }

    private void SetFriendsUnavailable()
    {
        ShellFriendsButton.Visibility = Visibility.Collapsed;
        _dashboardView.SetFriends(false, Array.Empty<GrevDadFriend>(), offline: false);
        if (_navigation.Current == Route.Friends) _navigation.GoBack();
    }
}
