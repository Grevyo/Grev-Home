using System.Windows;
using System.Windows.Controls;
using GrevHome.Sessions;

namespace GrevHome.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler? LogoutRequested;

    public DashboardView()
    {
        InitializeComponent();
    }

    public void SetPrimaryUser(SessionUser? user)
    {
        WelcomeText.Text = user is null ? "Welcome" : $"Welcome, {user.DisplayName}";
    }

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);

    private void Placeholder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string feature })
        {
            StatusText.Text = $"{feature} is intentionally a dashboard placeholder in 0.1. The tile exists now so controller navigation can be tested before feature logic is added.";
        }
    }
}
