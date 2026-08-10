using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record ProfileEditRequest(
    string GrevId,
    string DisplayName,
    string AvatarKey,
    AccountRole Role,
    string? CustomAvatarSourcePath);

public partial class ProfileEditView : UserControl
{
    private LocalProfile? _profile;
    private string _selectedAvatarKey = ProfileAvatarCatalog.DefaultKey;
    private AccountRole _selectedRole = AccountRole.Standard;
    private bool _canChangeRole;
    private string? _customAvatarSourcePath;

    public event Action<ProfileEditRequest>? SaveRequested;
    public event EventHandler? ChooseCustomPhotoRequested;
    public event EventHandler? KeyboardOpened;
    public event EventHandler? KeyboardClosed;

    public bool IsKeyboardOpen => KeyboardOverlay.IsOpen;

    public ProfileEditView()
    {
        InitializeComponent();
        BuildAvatarButtons();
        KeyboardOverlay.Completed += value =>
        {
            DisplayNameTextBox.Text = value;
            DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
            UpdateAvatarPresentation();
        };
        KeyboardOverlay.Opened += (_, _) => KeyboardOpened?.Invoke(this, EventArgs.Empty);
        KeyboardOverlay.Closed += (_, _) => KeyboardClosed?.Invoke(this, EventArgs.Empty);
    }

    public void SetProfile(LocalProfile profile, bool canChangeRole)
    {
        _profile = profile;
        _canChangeRole = canChangeRole;
        _selectedAvatarKey = ProfileAvatarCatalog.Normalize(profile.AvatarKey);
        _selectedRole = profile.Role;
        _customAvatarSourcePath = null;

        IdentityText.Text = $"@{profile.Username}  •  {profile.GrevId}  •  Username and GrevID are permanent";
        DisplayNameTextBox.Text = profile.DisplayName;
        DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;

        RolePanel.Visibility = canChangeRole ? Visibility.Visible : Visibility.Collapsed;
        RoleLockedText.Visibility = canChangeRole ? Visibility.Collapsed : Visibility.Visible;
        RoleLockedText.Text = $"Role: {profile.Role} • only an Admin can change account roles.";
        StatusText.Text = "Display Name and profile picture are local profile settings. Saving never renames the Username, GrevID or profile folder.";

        UpdateAvatarPresentation();
        UpdateRolePresentation();
    }

    public void SetCustomPhotoSource(string path)
    {
        _customAvatarSourcePath = path;
        _selectedAvatarKey = ProfileAvatarCatalog.CustomKey;
        UpdateAvatarPresentation();
        StatusText.Text = $"Selected custom photo: {Path.GetFileName(path)}. Save Profile to keep it.";
    }

    public void ShowStatus(string message) => StatusText.Text = message;
    public void CancelKeyboard() => KeyboardOverlay.Cancel();

    private void BuildAvatarButtons()
    {
        foreach (var preset in ProfileAvatarCatalog.Presets)
        {
            var button = new Button
            {
                Tag = preset.Key,
                Width = 68,
                Height = 60,
                Margin = new Thickness(3),
                FontSize = 17
            };
            button.Click += Avatar_Click;
            AvatarButtonsPanel.Children.Add(button);
        }
    }

    private void OpenKeyboard_Click(object sender, RoutedEventArgs e) =>
        KeyboardOverlay.Open("Change Display Name", DisplayNameTextBox.Text, DisplayNameTextBox.MaxLength);

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e) =>
        ChooseCustomPhotoRequested?.Invoke(this, EventArgs.Empty);

    private void Avatar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string avatarKey })
        {
            _selectedAvatarKey = ProfileAvatarCatalog.Normalize(avatarKey);
            _customAvatarSourcePath = null;
            UpdateAvatarPresentation();
        }
    }

    private void Role_Click(object sender, RoutedEventArgs e)
    {
        if (!_canChangeRole || sender is not Button { Tag: string roleName } ||
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

        AvatarPreviewImage.Source = null;
        AvatarPreviewImage.Visibility = Visibility.Collapsed;
        AvatarPreviewText.Visibility = Visibility.Visible;
        AvatarPreviewText.Text = ProfileAvatarCatalog.GetDisplayGlyph(_selectedAvatarKey, displayName);

        if (_selectedAvatarKey == ProfileAvatarCatalog.CustomKey)
        {
            var source = TryLoadPendingCustomImage() ?? (_profile is null ? null : ProfileAvatarCatalog.TryLoadCustomImage(_profile));
            if (source is not null)
            {
                AvatarPreviewImage.Source = source;
                AvatarPreviewImage.Visibility = Visibility.Visible;
                AvatarPreviewText.Visibility = Visibility.Collapsed;
            }
            AvatarChoiceText.Text = "Custom photo";
        }
        else
        {
            var selectedPreset = ProfileAvatarCatalog.Presets.First(item => item.Key == _selectedAvatarKey);
            AvatarChoiceText.Text = selectedPreset.Name;
        }

        foreach (var button in AvatarButtonsPanel.Children.OfType<Button>())
        {
            if (button.Tag is not string key) continue;
            var preset = ProfileAvatarCatalog.Presets.First(item => item.Key == key);
            var selected = string.Equals(key, _selectedAvatarKey, StringComparison.OrdinalIgnoreCase);
            var glyph = preset.Key == ProfileAvatarCatalog.DefaultKey ? "Aa" : preset.Glyph;
            button.Content = selected ? $"✓{glyph}" : glyph;
        }
    }

    private BitmapImage? TryLoadPendingCustomImage()
    {
        if (string.IsNullOrWhiteSpace(_customAvatarSourcePath) || !File.Exists(_customAvatarSourcePath)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(_customAvatarSourcePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
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
        if (_profile is null) return;
        SaveRequested?.Invoke(new ProfileEditRequest(
            _profile.GrevId,
            DisplayNameTextBox.Text,
            _selectedAvatarKey,
            _selectedRole,
            _customAvatarSourcePath));
    }
}
