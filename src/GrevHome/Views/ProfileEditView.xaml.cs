using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record ProfileEditRequest(
    string GrevId,
    string DisplayName,
    string StatusMessage,
    string Bio,
    string AvatarKey,
    AccountRole Role,
    string? CustomAvatarSourcePath);

public partial class ProfileEditView : UserControl
{
    private enum KeyboardTarget
    {
        DisplayName,
        StatusMessage,
        Bio
    }

    private LocalProfile? _profile;
    private string _selectedAvatarKey = ProfileAvatarCatalog.DefaultKey;
    private AccountRole _selectedRole = AccountRole.Standard;
    private bool _canChangeRole;
    private string? _customAvatarSourcePath;
    private KeyboardTarget _keyboardTarget = KeyboardTarget.DisplayName;

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
            switch (_keyboardTarget)
            {
                case KeyboardTarget.DisplayName:
                    DisplayNameTextBox.Text = value;
                    DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
                    UpdateAvatarPresentation();
                    break;
                case KeyboardTarget.StatusMessage:
                    StatusMessageTextBox.Text = value;
                    StatusMessageTextBox.CaretIndex = StatusMessageTextBox.Text.Length;
                    break;
                case KeyboardTarget.Bio:
                    BioTextBox.Text = value;
                    BioTextBox.CaretIndex = BioTextBox.Text.Length;
                    break;
            }
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
        StatusMessageTextBox.Text = profile.StatusMessage ?? string.Empty;
        StatusMessageTextBox.CaretIndex = StatusMessageTextBox.Text.Length;
        BioTextBox.Text = profile.Bio ?? string.Empty;
        BioTextBox.CaretIndex = BioTextBox.Text.Length;

        RolePanel.Visibility = Visibility.Visible;
        AdminRoleButton.IsEnabled = canChangeRole;
        StandardRoleButton.IsEnabled = canChangeRole;
        GuestRoleButton.IsEnabled = canChangeRole;
        RoleLockedText.Visibility = canChangeRole ? Visibility.Collapsed : Visibility.Visible;
        RoleLockedText.Text = profile.Role == AccountRole.Guest
            ? "Guest role and its grey profile border are locked for this session. An Admin must change the account role."
            : $"Role: {profile.Role} • only an Admin can change account roles and their profile-border style.";

        StatusText.Text = "Display Name, status, About and profile picture are local profile settings. Saving never renames the Username, GrevID or profile folder.";
        UpdateAvatarPresentation();
        UpdateRolePresentation();
    }

    public ProfileEditRequest? CaptureDraft() => _profile is null
        ? null
        : new ProfileEditRequest(
            _profile.GrevId,
            DisplayNameTextBox.Text,
            StatusMessageTextBox.Text,
            BioTextBox.Text,
            _selectedAvatarKey,
            _selectedRole,
            _customAvatarSourcePath);

    public void RestoreDraft(ProfileEditRequest draft)
    {
        if (_profile is null || !string.Equals(_profile.GrevId, draft.GrevId, StringComparison.OrdinalIgnoreCase)) return;
        DisplayNameTextBox.Text = draft.DisplayName;
        StatusMessageTextBox.Text = draft.StatusMessage;
        BioTextBox.Text = draft.Bio;
        _selectedAvatarKey = ProfileAvatarCatalog.Normalize(draft.AvatarKey);
        _selectedRole = draft.Role;
        _customAvatarSourcePath = draft.CustomAvatarSourcePath;
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
            var button = new Button { Tag = preset.Key, Width = 68, Height = 60, Margin = new Thickness(3), FontSize = 17 };
            button.Click += Avatar_Click;
            AvatarButtonsPanel.Children.Add(button);
        }
    }

    private void OpenKeyboard_Click(object sender, RoutedEventArgs e)
    {
        _keyboardTarget = KeyboardTarget.DisplayName;
        KeyboardOverlay.Open("Change Display Name", DisplayNameTextBox.Text, DisplayNameTextBox.MaxLength);
    }

    private void OpenStatusKeyboard_Click(object sender, RoutedEventArgs e)
    {
        _keyboardTarget = KeyboardTarget.StatusMessage;
        KeyboardOverlay.Open("Edit Status / Tagline", StatusMessageTextBox.Text, StatusMessageTextBox.MaxLength);
    }

    private void OpenBioKeyboard_Click(object sender, RoutedEventArgs e)
    {
        _keyboardTarget = KeyboardTarget.Bio;
        KeyboardOverlay.Open("Edit About", BioTextBox.Text, BioTextBox.MaxLength);
    }

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e) => ChooseCustomPhotoRequested?.Invoke(this, EventArgs.Empty);

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
        if (!_canChangeRole || sender is not Button { Tag: string roleName } || !Enum.TryParse<AccountRole>(roleName, true, out var role)) return;
        _selectedRole = role;
        UpdateRolePresentation();
    }

    private void UpdateAvatarPresentation()
    {
        var displayName = string.IsNullOrWhiteSpace(DisplayNameTextBox.Text) ? _profile?.DisplayName ?? "?" : DisplayNameTextBox.Text;
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
            AvatarChoiceText.Text = ProfileAvatarCatalog.Presets.First(item => item.Key == _selectedAvatarKey).Name;
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
        catch { return null; }
    }

    private void UpdateRolePresentation()
    {
        RoleDescriptionText.Text = $"{AccountAuthorizationService.DescribeRole(_selectedRole)}  •  {DescribeRoleBorder(_selectedRole)}";
        AdminRoleButton.Content = _selectedRole == AccountRole.Admin ? "✓ Admin" : "Admin";
        StandardRoleButton.Content = _selectedRole == AccountRole.Standard ? "✓ Standard" : "Standard";
        GuestRoleButton.Content = _selectedRole == AccountRole.Guest ? "✓ Guest" : "Guest";

        var roleBrush = GetRoleBrush(_selectedRole);
        ProfileEditCard.BorderBrush = roleBrush;
        AvatarPreviewBorder.BorderBrush = roleBrush;
        ProfileEditCard.Effect = CreateRoleEffect(_selectedRole, roleBrush.Color);
    }

    private SolidColorBrush GetRoleBrush(AccountRole role) =>
        (SolidColorBrush)FindResource(role switch
        {
            AccountRole.Admin => "AdminRoleBrush",
            AccountRole.Standard => "StandardRoleBrush",
            _ => "GuestRoleBrush"
        });

    private static DropShadowEffect? CreateRoleEffect(AccountRole role, Color color) => role switch
    {
        AccountRole.Admin => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 18,
            ShadowDepth = 0,
            Opacity = 0.52
        },
        AccountRole.Standard => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 9,
            ShadowDepth = 0,
            Opacity = 0.22
        },
        _ => null
    };

    private static string DescribeRoleBorder(AccountRole role) => role switch
    {
        AccountRole.Admin => "Gold profile border with a gold glow.",
        AccountRole.Standard => "Red profile border.",
        AccountRole.Guest => "Fixed grey profile border.",
        _ => "Grey profile border."
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var draft = CaptureDraft();
        if (draft is not null) SaveRequested?.Invoke(draft);
    }
}
