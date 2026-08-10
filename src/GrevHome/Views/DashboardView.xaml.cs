using System.Windows;
using System.Windows.Controls;
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

    public DashboardView()
    {
        InitializeComponent();
    }

    public void SetSession(SessionContext session)
    {
        var primary = session.PrimaryUser;
        WelcomeText.Text = primary is null ? "Welcome" : $"Welcome, {primary.DisplayName}";

        if (!session.HasSignedInUsers)
        {
            SessionUsersText.Text = "No active session";
            return;
        }

        var signedIn = string.Join(", ", session.SignedInUsers.Select(user =>
            user.IsPrimary ? $"★ {user.DisplayName}" : user.DisplayName));
        SessionUsersText.Text = $"Signed in: {signedIn}";
    }

    public void SetRunningCount(int runningCount)
    {
        RunningAppsButton.Content = $"Running Apps\n{runningCount} active";
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

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);

    private void Placeholder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string feature })
        {
            StatusText.Text = $"{feature} is still a dashboard placeholder while Grev Home's backbone is built first.";
        }
    }
}
