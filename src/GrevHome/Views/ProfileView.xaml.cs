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

    public void SetProfile(LocalProfile? profile, string? sessionStatus = null, bool canEdit = true)
    {
        if (profile is null)
        {
            AvatarText.Text = "?";
            DisplayNameText.Text = "No local profile";
            UsernameText.Text = "No local profile is available.";
            RoleText.Text = string.Empty;
            SessionText.Text = string.Empty;
            GrevIdText.Text = "—";
            CreatedText.Text = "—";
            RoleDescriptionText.Text = "—";
            PermissionsText.Text = "—";
            EditProfileButton.IsEnabled = false;
            return;
        }

        AvatarText.Text = ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName);
        DisplayNameText.Text = profile.DisplayName;
        UsernameText.Text = $"@{profile.Username}";
        RoleText.Text = profile.Role.ToString().ToUpperInvariant();
        SessionText.Text = sessionStatus ?? "Not currently signed in";
        GrevIdText.Text = profile.GrevId;
        CreatedText.Text = profile.CreatedAtUtc.ToLocalTime().ToString("g");
        RoleDescriptionText.Text = AccountAuthorizationService.DescribeRole(profile.Role);
        PermissionsText.Text = AccountAuthorizationService.SummarizePermissions(profile.Role);
        EditProfileButton.IsEnabled = canEdit;
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e) =>
        EditProfileRequested?.Invoke(this, EventArgs.Empty);
}
