using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileView : UserControl
{
    public event EventHandler? EditProfileRequested;

    public ProfileView()
    {
        InitializeComponent();
    }

    public void SetProfile(LocalProfile? profile)
    {
        if (profile is null)
        {
            DisplayNameText.Text = "No local profile";
            UsernameText.Text = "No local Primary User is available.";
            RoleText.Text = string.Empty;
            GrevIdText.Text = "—";
            CreatedText.Text = "—";
            return;
        }

        DisplayNameText.Text = profile.DisplayName;
        UsernameText.Text = $"@{profile.Username}";
        RoleText.Text = profile.Role.ToString().ToUpperInvariant();
        GrevIdText.Text = profile.GrevId;
        CreatedText.Text = profile.CreatedAtUtc.ToLocalTime().ToString("g");
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e) =>
        EditProfileRequested?.Invoke(this, EventArgs.Empty);
}
