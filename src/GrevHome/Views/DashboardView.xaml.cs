using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrevHome.Dashboard;
using GrevHome.Notifications;
using GrevHome.Profiles;
using GrevHome.Sessions;
using GrevHome.Transfers;
using GrevHome.Presentation;
using GrevHome.Online;

namespace GrevHome.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler? LogoutRequested;
    public event EventHandler? ManageUsersRequested;
    public event EventHandler? InstalledAppsRequested;
    public event EventHandler? YourGamesRequested;
    public event EventHandler? RunningAppsRequested;
    public event EventHandler? AppKillerRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AdminConsoleRequested;
    public event EventHandler? FilesRequested;
    public event EventHandler? GrevDadRequested;
    public event EventHandler? StoreRequested;
    public event EventHandler? ActivityCenterRequested;
    public event Action<string>? ActivityAppRequested;
    public event Action<string>? TileSettingsRequested;
    public event EventHandler? FriendsRequested;
    private IReadOnlyDictionary<string, ResolvedDashboardTile> _tilePresentations = new Dictionary<string, ResolvedDashboardTile>();
    private Button? _pendingTileButton;
    private string? _pendingTileId;
    private bool _pendingTileLongPress;

    public DashboardView()
    {
        InitializeComponent();
        SetDashboardData(DashboardDataSnapshot.Empty);
        SetSystemActivity(NotificationSnapshot.Empty, TransferSnapshot.Empty);
    }

    public void SetSession(SessionContext session)
    {
        var primary = session.PrimaryUser;
        WelcomeText.Text = primary is null ? "Welcome" : $"Welcome, {primary.DisplayName}";
        AdminConsoleButton.Visibility = primary?.Role == AccountRole.Admin
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!session.HasSignedInUsers)
        {
            SessionUsersText.Text = "No active session";
            return;
        }

        var signedIn = string.Join(", ", session.SignedInUsers.Select(user =>
            user.IsPrimary ? $"★ {user.DisplayName}" : user.DisplayName));
        SessionUsersText.Text = session.SignedInUsers.Count == 1
            ? $"Signed in: {signedIn}"
            : $"{session.SignedInUsers.Count} players signed in: {signedIn}";
    }

    public void SetRunningCount(int runningCount)
    {
        RunningCountText.Text = $"{runningCount} active";
        RenderDashboardTiles();
    }

    public void SetDashboardData(DashboardDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ActivitySummaryText.Text = snapshot.AppsPlayed == 0
            ? "No completed app sessions recorded for this account yet."
            : $"{FormatDuration(snapshot.TotalPlaytimeSeconds)} total  •  {snapshot.TotalSessions} session{(snapshot.TotalSessions == 1 ? string.Empty : "s")}  •  {snapshot.AppsPlayed} app{(snapshot.AppsPlayed == 1 ? string.Empty : "s")} played";

        if (snapshot.ContinueApp is { } continueApp)
        {
            ContinueButton.Visibility = Visibility.Visible;
            ContinueButton.Tag = continueApp.AppId;
            ContinueButton.Padding = new Thickness(0, 0, 0, 0);
            ContinueButton.Content = CreateActivityTile(continueApp);
        }
        else
        {
            ContinueButton.Visibility = Visibility.Collapsed;
            ContinueButton.Tag = null;
            ContinueButton.Content = null;
        }

        RecentAppsPanel.Children.Clear();
        var recentItems = snapshot.RecentlyUsed
            .Where(item => snapshot.ContinueApp is null ||
                           !string.Equals(item.AppId, snapshot.ContinueApp.AppId, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        foreach (var item in recentItems)
        {
            RecentAppsPanel.Children.Add(CreateRecentAppButton(item));
        }

        ActivitySection.Visibility = snapshot.AppsPlayed > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetSystemActivity(NotificationSnapshot notifications, TransferSnapshot transfers)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(transfers);

        var parts = new List<string>();
        if (notifications.UnreadCount > 0)
        {
            parts.Add($"{notifications.UnreadCount} unread");
        }
        if (transfers.ActiveCount > 0)
        {
            parts.Add($"{transfers.ActiveCount} downloading");
        }
        if (transfers.QueuedCount > 0)
        {
            parts.Add($"{transfers.QueuedCount} queued");
        }
        if (transfers.FailedCount > 0)
        {
            parts.Add($"{transfers.FailedCount} failed");
        }

        ActivityCenterDetailText.Text = parts.Count == 0
            ? "Notifications and downloads"
            : string.Join("  •  ", parts);
        RenderDashboardTiles();
    }

    public void SetTilePresentations(IReadOnlyDictionary<string, ResolvedDashboardTile> presentations)
    {
        _tilePresentations = presentations;
        RenderDashboardTiles();
    }

    public void SetFriends(bool available, IReadOnlyList<GrevDadFriend> friends, bool offline)
    {
        FriendsSection.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        FriendsPanel.Children.Clear();
        if (!available) return;
        var online = friends.Count(friend => !string.Equals(friend.Presence.Availability, "offline", StringComparison.OrdinalIgnoreCase));
        FriendsSummaryText.Text = offline ? $"Offline • {friends.Count} cached" : $"{online} online • {friends.Count} total";
        foreach (var friend in friends.OrderByDescending(item => !string.Equals(item.Presence.Availability, "offline", StringComparison.OrdinalIgnoreCase)).ThenBy(item => item.DisplayName).Take(6))
        {
            FriendsPanel.Children.Add(new Border
            {
                Width = 285, Height = 86, Margin = new Thickness(8), Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(9),
                Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
                Child = new StackPanel { Children = { new TextBlock { Text = friend.DisplayName, FontSize = 18, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = $"{friend.Presence.Availability}  •  {friend.Presence.ActivityText}", Margin = new Thickness(0,6,0,0), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis } } }
            });
        }
    }

    public bool BeginControllerTilePress()
    {
        if (Keyboard.FocusedElement is not Button { Tag: string tileId } button ||
            !DashboardTileCatalog.All.Any(item => item.Id == tileId)) return false;
        _pendingTileButton = button;
        _pendingTileId = tileId;
        _pendingTileLongPress = false;
        return true;
    }

    public void HandleControllerTileLongPress()
    {
        if (_pendingTileButton is null || _pendingTileId is null) return;
        _pendingTileLongPress = true;
        TileSettingsRequested?.Invoke(_pendingTileId);
    }

    public void CompleteControllerTilePress()
    {
        var button = _pendingTileButton;
        var longPress = _pendingTileLongPress;
        _pendingTileButton = null; _pendingTileId = null; _pendingTileLongPress = false;
        if (!longPress) button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public void CancelControllerTilePress()
    {
        _pendingTileButton = null; _pendingTileId = null; _pendingTileLongPress = false;
    }

    public bool SuppressPendingTileClick(object sender) => sender == _pendingTileButton;

    private void RenderDashboardTiles()
    {
        RenderDashboardTile(YourGamesButton, "your-games", GamesSummaryText.Text);
        RenderDashboardTile(InstalledAppsButton, "installed-apps", DashboardTileCatalog.Get("installed-apps").Detail);
        RenderDashboardTile(StoreButton, "grev-store", DashboardTileCatalog.Get("grev-store").Detail);
        RenderDashboardTile(FilesButton, "files", DashboardTileCatalog.Get("files").Detail);
        RenderDashboardTile(GrevDadButton, "grev-dad", DashboardTileCatalog.Get("grev-dad").Detail);
        RenderDashboardTile(RunningAppsButton, "running-apps", RunningCountText.Text);
        RenderDashboardTile(ActivityCenterButton, "activity-center", ActivityCenterDetailText.Text);
        RenderDashboardTile(AppKillerButton, "app-killer", DashboardTileCatalog.Get("app-killer").Detail);
        RenderDashboardTile(SettingsButton, "settings", DashboardTileCatalog.Get("settings").Detail);
        RenderDashboardTile(AdminConsoleButton, "admin-console", DashboardTileCatalog.Get("admin-console").Detail);
    }

    private void RenderDashboardTile(Button button, string id, string detail)
    {
        var definition = DashboardTileCatalog.Get(id);
        var tile = _tilePresentations.TryGetValue(id, out var resolved) ? resolved : new ResolvedDashboardTile(id, definition.Name, detail, definition.Color, null, definition.IconAsset, false);
        button.Padding = new Thickness(0);
        if (!string.IsNullOrWhiteSpace(tile.TileMediaPath)) button.Content = AppArtworkFactory.CreateFullTile(tile.TileMediaPath, tile.TileColor, 285, 145);
        else button.Content = AppArtworkFactory.CreateTile(tile.DisplayName, tile.IconAsset, tile.TileColor);
        button.ToolTip = $"{tile.DisplayName} • {detail} • Hold A or right-click for appearance settings";
    }

    private void DashboardTile_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: string tileId }) { e.Handled = true; TileSettingsRequested?.Invoke(tileId); }
    }

    public void ShowStatus(string message)
    {
        DashboardStatusText.Text = message;
        DashboardStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private Button CreateRecentAppButton(DashboardAppActivity item)
    {
        var button = new Button
        {
            Style = (Style)FindResource("DashboardTileStyle"),
            Tag = item.AppId,
            IsEnabled = item.CanLaunch,
            ToolTip = item.CanLaunch
                ? null
                : item.IsInstalled
                    ? item.AvailabilityMessage ?? "This app is not available to the current Primary User."
                    : "This app is no longer installed."
        };

        button.Padding = new Thickness(0, 0, 0, 0);
        button.Content = CreateActivityTile(item);
        button.Click += ActivityApp_Click;
        return button;
    }

    private static FrameworkElement CreateActivityTile(DashboardAppActivity item)
    {
        var presentation = item.Presentation;
        if (!string.IsNullOrWhiteSpace(presentation?.TileMediaPath))
        {
            var fullTile = AppArtworkFactory.CreateFullTile(
                presentation.TileMediaPath,
                presentation.TileColor,
                285,
                145);
            var fullGrid = new Grid();
            fullGrid.Children.Add(fullTile);
            AddGameConsoleLogo(fullGrid, item);
            fullGrid.Children.Add(CreateLastPlayedTimestamp(item));
            return fullGrid;
        }

        var tile = AppArtworkFactory.CreateTile(
            presentation?.DisplayName ?? item.AppName,
            item.AppId.StartsWith("game.", StringComparison.OrdinalIgnoreCase)
                ? null
                : presentation?.TileMediaPath ?? presentation?.IconPath,
            presentation?.TileColor);
        var grid = new Grid();
        grid.Children.Add(tile);
        AddGameConsoleLogo(grid, item);
        grid.Children.Add(CreateLastPlayedTimestamp(item));
        return grid;
    }

    private static void AddGameConsoleLogo(Grid grid, DashboardAppActivity item)
    {
        if (item.Game is not null)
        {
            grid.Children.Add(GameArtworkFactory.CreateConsoleMark(item.Game, item.CanLaunch));
        }
    }

    private static FrameworkElement CreateLastPlayedTimestamp(DashboardAppActivity item)
    {
        return new TextBlock
        {
            Text = item.LastPlayedAtUtc.ToLocalTime().ToString("d MMM yyyy  HH:mm"),
            Margin = new Thickness(8, 6, 8, 0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Opacity = 0.95
            }
        };
    }

    private void ActivityApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string appId } && !string.IsNullOrWhiteSpace(appId))
        {
            ActivityAppRequested?.Invoke(appId);
        }
    }

    private static string FormatDuration(long totalSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return "<1m";
    }

    private static string FormatLastPlayed(DateTimeOffset playedAtUtc)
    {
        var local = playedAtUtc.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
        {
            return $"today {local:HH:mm}";
        }

        if (local.Date == today.AddDays(-1))
        {
            return $"yesterday {local:HH:mm}";
        }

        return local.ToString("d MMM yyyy");
    }

    private void ManageUsers_Click(object sender, RoutedEventArgs e) =>
        ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    private void InstalledApps_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, InstalledAppsRequested);

    private void YourGames_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, YourGamesRequested);

    private void RunningApps_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, RunningAppsRequested);

    private void ActivityCenter_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, ActivityCenterRequested);

    private void AppKiller_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, AppKillerRequested);

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, SettingsRequested);

    private void AdminConsole_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, AdminConsoleRequested);

    private void Files_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, FilesRequested);

    private void Store_Click(object sender, RoutedEventArgs e) =>
        InvokeUnlessPending(sender, StoreRequested);

    private void GrevDad_Click(object sender, RoutedEventArgs e) => InvokeUnlessPending(sender,GrevDadRequested);

    private void InvokeUnlessPending(object sender, EventHandler? handler)
    {
        if (!SuppressPendingTileClick(sender)) handler?.Invoke(this, EventArgs.Empty);
    }

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);

    private void Friends_Click(object sender, RoutedEventArgs e) => FriendsRequested?.Invoke(this, EventArgs.Empty);
}
