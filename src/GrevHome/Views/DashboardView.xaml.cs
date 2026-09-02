using System.Windows;
using System.Windows.Controls;
using GrevHome.Dashboard;
using GrevHome.Notifications;
using GrevHome.Profiles;
using GrevHome.Sessions;
using GrevHome.Transfers;
using GrevHome.Presentation;

namespace GrevHome.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler? LogoutRequested;
    public event EventHandler? ManageUsersRequested;
    public event EventHandler? InstalledAppsRequested;
    public event EventHandler? RunningAppsRequested;
    public event EventHandler? AppKillerRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AdminConsoleRequested;
    public event EventHandler? FilesRequested;
    public event EventHandler? StoreRequested;
    public event EventHandler? ActivityCenterRequested;
    public event Action<string>? ActivityAppRequested;

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
            ContinueButton.Content = CreateActivityTile(continueApp, "CONTINUE");
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

        button.Content = CreateActivityTile(item, "RECENT");
        button.Click += ActivityApp_Click;
        return button;
    }

    private static FrameworkElement CreateActivityTile(DashboardAppActivity item, string badge)
    {
        var presentation = item.Presentation;
        var tile = AppArtworkFactory.CreateTile(
            presentation?.DisplayName ?? item.AppName,
            presentation?.TileMediaPath ?? presentation?.IconPath,
            presentation?.TileColor);
        var grid = new Grid();
        grid.Children.Add(tile);
        var detail = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(205, 8, 12, 20)),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(5),
            Child = new TextBlock { Text = badge, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White }
        };
        grid.Children.Add(detail);
        return grid;
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
        InstalledAppsRequested?.Invoke(this, EventArgs.Empty);

    private void RunningApps_Click(object sender, RoutedEventArgs e) =>
        RunningAppsRequested?.Invoke(this, EventArgs.Empty);

    private void ActivityCenter_Click(object sender, RoutedEventArgs e) =>
        ActivityCenterRequested?.Invoke(this, EventArgs.Empty);

    private void AppKiller_Click(object sender, RoutedEventArgs e) =>
        AppKillerRequested?.Invoke(this, EventArgs.Empty);

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void AdminConsole_Click(object sender, RoutedEventArgs e) =>
        AdminConsoleRequested?.Invoke(this, EventArgs.Empty);

    private void Files_Click(object sender, RoutedEventArgs e) =>
        FilesRequested?.Invoke(this, EventArgs.Empty);

    private void Store_Click(object sender, RoutedEventArgs e) =>
        StoreRequested?.Invoke(this, EventArgs.Empty);

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);
}
