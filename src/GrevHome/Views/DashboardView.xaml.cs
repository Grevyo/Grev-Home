using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;
using GrevHome.Sessions;

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

    public DashboardView()
    {
        InitializeComponent();
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

    public void ShowStatus(string message)
    {
        DashboardStatusText.Text = message;
        DashboardStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ManageUsers_Click(object sender, RoutedEventArgs e) =>
        ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    private void InstalledApps_Click(object sender, RoutedEventArgs e) =>
        InstalledAppsRequested?.Invoke(this, EventArgs.Empty);

    private void RunningApps_Click(object sender, RoutedEventArgs e) =>
        RunningAppsRequested?.Invoke(this, EventArgs.Empty);

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
