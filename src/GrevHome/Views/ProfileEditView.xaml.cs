using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record ProfileEditRequest(
    string GrevId,
    string DisplayName,
    string AvatarKey,
    AccountRole Role);

public partial class ProfileEditView : UserControl
{
    private LocalProfile? _profile;
    private string _selectedAvatarKey = ProfileAvatarCatalog.DefaultKey;
    private AccountRole _selectedRole = AccountRole.Standard;
    private bool _canChangeRole;

    public event Action<ProfileEditRequest>? SaveRequested;

    public ProfileEditView()
    {
        InitializeComponent();
        BuildKeyboard();
        BuildAvatarButtons();
    }

    public void SetProfile(LocalProfile profile, bool canChangeRole)
    {
        _profile = profile;
        _canChangeRole = canChangeRole;
        _selectedAvatarKey = ProfileAvatarCatalog.Normalize(profile.AvatarKey);
        _selectedRole = profile.Role;

        IdentityText.Text = $"@{profile.Username}  •  {profile.GrevId}  •  Username and GrevID are permanent";
        DisplayNameTextBox.Text = profile.DisplayName;
        DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;

        RolePanel.Visibility = canChangeRole ? Visibility.Visible : Visibility.Collapsed;
        RoleLockedText.Visibility = canChangeRole ? Visibility.Collapsed : Visibility.Visible;
        RoleLockedText.Text = $"Role: {profile.Role} • only an Admin can change account roles.";
        StatusText.Text = "Display Name and avatar are local profile settings. Saving never renames the Username, GrevID or profile folder.";

        UpdateAvatarPresentation();
        UpdateRolePresentation();
    }

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    private void BuildKeyboard()
    {
        const string keys = "QWERTYUIOPASDFGHJKLZXCVBNM1234567890";
        foreach (var key in keys)
        {
            var button = new Button
            {
                Content = key.ToString(),
                Tag = key,
                Width = 48,
                Height = 48,
                Margin = new Thickness(3),
                FontSize = 16
            };
            button.Click += KeyboardKey_Click;
            KeyboardPanel.Children.Add(button);
        }
    }

    private void BuildAvatarButtons()
    {
        foreach (var preset in ProfileAvatarCatalog.Presets)
        {
            var button = new Button
            {
                Tag = preset.Key,
                Width = 108,
                Height = 52,
                Margin = new Thickness(3)
            };
            button.Click += Avatar_Click;
            AvatarButtonsPanel.Children.Add(button);
        }
    }

    private void KeyboardKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: char key } && DisplayNameTextBox.Text.Length < DisplayNameTextBox.MaxLength)
        {
            DisplayNameTextBox.Text += key;
            DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
            UpdateAvatarPresentation();
        }
    }

    private void Space_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayNameTextBox.Text.Length < DisplayNameTextBox.MaxLength)
        {
            DisplayNameTextBox.Text += " ";
            DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
            UpdateAvatarPresentation();
        }
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayNameTextBox.Text.Length == 0)
        {
            return;
        }

        DisplayNameTextBox.Text = DisplayNameTextBox.Text[..^1];
        DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
        UpdateAvatarPresentation();
    }

    private void Avatar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string avatarKey })
        {
            _selectedAvatarKey = ProfileAvatarCatalog.Normalize(avatarKey);
            UpdateAvatarPresentation();
        }
    }

    private void Role_Click(object sender, RoutedEventArgs e)
    {
        if (!_canChangeRole ||
            sender is not Button { Tag: string roleName } ||
            !Enum.TryParse<AccountRole>(roleName, true, out var role))
        {
            return;
        }

        _selectedRole = role;
        UpdateRolePresentation();
    }

    private void UpdateAvatarPresentation()
    {
        var displayName = string.IsNullOrWhiteSpace(DisplayNameTextBox.Text)
            ? _profile?.DisplayName ?? "?"
            : DisplayNameTextBox.Text;
        AvatarPreviewText.Text = ProfileAvatarCatalog.GetDisplayGlyph(_selectedAvatarKey, displayName);

        foreach (var button in AvatarButtonsPanel.Children.OfType<Button>())
        {
            if (button.Tag is not string key)
            {
                continue;
            }

            var preset = ProfileAvatarCatalog.Presets.First(item => item.Key == key);
            var selected = string.Equals(key, _selectedAvatarKey, StringComparison.OrdinalIgnoreCase);
            var glyph = preset.Key == ProfileAvatarCatalog.DefaultKey ? "Aa" : preset.Glyph;
            button.Content = selected ? $"✓ {glyph} {preset.Name}" : $"{glyph} {preset.Name}";
        }
    }

    private void UpdateRolePresentation()
    {
        RoleDescriptionText.Text = AccountAuthorizationService.DescribeRole(_selectedRole);
        AdminRoleButton.Content = _selectedRole == AccountRole.Admin ? "✓ Admin" : "Admin";
        StandardRoleButton.Content = _selectedRole == AccountRole.Standard ? "✓ Standard" : "Standard";
        GuestRoleButton.Content = _selectedRole == AccountRole.Guest ? "✓ Guest" : "Guest";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            return;
        }

        SaveRequested?.Invoke(new ProfileEditRequest(
            _profile.GrevId,
            DisplayNameTextBox.Text,
            _selectedAvatarKey,
            _selectedRole));
    }
}
