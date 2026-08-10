using System.Windows;
using System.Windows.Controls;
using GrevHome.Sessions;

namespace GrevHome.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler? LogoutRequested;
    public event EventHandler? ManageUsersRequested;
    public event EventHandler? InstalledAppsRequested;

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

    private void ManageUsers_Click(object sender, RoutedEventArgs e) =>
        ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    private void InstalledApps_Click(object sender, RoutedEventArgs e) =>
        InstalledAppsRequested?.Invoke(this, EventArgs.Empty);

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);

    private void Placeholder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string feature })
        {
            StatusText.Text = $"{feature} is still a dashboard placeholder. App installation and launching are being layered on top of the GrevID storage/session foundation rather than hard-coded into Dashboard.";
        }
    }
}
