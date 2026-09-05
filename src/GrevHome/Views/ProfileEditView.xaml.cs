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
    string? CustomAvatarSourcePath,
    string BannerKey,
    ProfileShowcaseMode ShowcaseMode,
    string? CustomBannerSourcePath);

public partial class ProfileEditView : UserControl
{
    private enum KeyboardTarget
    {
        DisplayName,
        StatusMessage,
        Bio
    }

    private LocalProfile? _profile;
    private ProfilePresentationSettings _presentation = ProfilePresentationSettings.Default;
    private string _selectedAvatarKey = ProfileAvatarCatalog.DefaultKey;
    private string _selectedBannerKey = ProfileBannerCatalog.DefaultKey;
    private ProfileShowcaseMode _selectedShowcaseMode = ProfileShowcaseMode.TopPlayed;
    private AccountRole _selectedRole = AccountRole.Standard;
    private bool _canChangeRole;
    private string? _customAvatarSourcePath;
    private string? _customBannerSourcePath;
    private KeyboardTarget _keyboardTarget = KeyboardTarget.DisplayName;

    public event Action<ProfileEditRequest>? SaveRequested;
    public event EventHandler? ChooseCustomPhotoRequested;
    public event EventHandler? ChooseCustomBannerRequested;
    public event EventHandler? KeyboardOpened;
    public event EventHandler? KeyboardClosed;

    public bool IsKeyboardOpen => KeyboardOverlay.IsOpen;

    public ProfileEditView()
    {
        InitializeComponent();
        BuildAvatarButtons();
        BuildBannerButtons();
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

    public void SetProfile(
        LocalProfile profile,
        bool canChangeRole,
        ProfilePresentationSettings? presentation = null)
    {
        _profile = profile;
        _presentation = presentation ?? ProfilePresentationSettings.Default;
        _canChangeRole = canChangeRole;
        _selectedAvatarKey = ProfileAvatarCatalog.Normalize(profile.AvatarKey);
        _selectedBannerKey = ProfileBannerCatalog.Normalize(_presentation.BannerKey);
        _selectedShowcaseMode = _presentation.ShowcaseMode;
        _selectedRole = profile.Role;
        _customAvatarSourcePath = null;
        _customBannerSourcePath = null;
        var pictureOnly = profile.IsBuiltInGuest;
        BannerSettingsSection.Visibility = pictureOnly ? Visibility.Collapsed : Visibility.Visible;
        ProfileDetailsSection.Visibility = pictureOnly ? Visibility.Collapsed : Visibility.Visible;
        ProfileAppearanceRoleSection.Visibility = pictureOnly ? Visibility.Collapsed : Visibility.Visible;
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

        StatusText.Text = pictureOnly
            ? "This is Grev Home's built-in living-room Guest. Its identity and permissions are fixed; only its picture can be changed."
            : "Display Name, status, About, picture, banner and showcase are local profile settings. Saving never renames the Username, GrevID or profile folder.";
        UpdateAvatarPresentation();
        UpdateBannerPresentation();
        UpdateShowcasePresentation();
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
            _customAvatarSourcePath,
            _selectedBannerKey,
            _selectedShowcaseMode,
            _customBannerSourcePath);

    public void RestoreDraft(ProfileEditRequest draft)
    {
        if (_profile is null || !string.Equals(_profile.GrevId, draft.GrevId, StringComparison.OrdinalIgnoreCase)) return;
        DisplayNameTextBox.Text = draft.DisplayName;
        StatusMessageTextBox.Text = draft.StatusMessage;
        BioTextBox.Text = draft.Bio;
        _selectedAvatarKey = ProfileAvatarCatalog.Normalize(draft.AvatarKey);
        _selectedRole = draft.Role;
        _customAvatarSourcePath = draft.CustomAvatarSourcePath;
        _selectedBannerKey = ProfileBannerCatalog.Normalize(draft.BannerKey);
        _selectedShowcaseMode = draft.ShowcaseMode;
        _customBannerSourcePath = draft.CustomBannerSourcePath;
        UpdateAvatarPresentation();
        UpdateBannerPresentation();
        UpdateShowcasePresentation();
        UpdateRolePresentation();
    }

    public void SetCustomPhotoSource(string path)
    {
        _customAvatarSourcePath = path;
        _selectedAvatarKey = ProfileAvatarCatalog.CustomKey;
        UpdateAvatarPresentation();
        StatusText.Text = $"Selected custom photo: {Path.GetFileName(path)}. Save Profile to keep it.";
    }

    public void SetCustomBannerSource(string path)
    {
        _customBannerSourcePath = path;
        _selectedBannerKey = ProfileBannerCatalog.CustomKey;
        UpdateBannerPresentation();
        StatusText.Text = $"Selected custom banner: {Path.GetFileName(path)}. Save Profile to keep it.";
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

    private void BuildBannerButtons()
    {
        foreach (var preset in ProfileBannerCatalog.Presets)
        {
            var button = new Button
            {
                Tag = preset.Key,
                Content = preset.Name,
                MinWidth = 132,
                Height = 46,
                Margin = new Thickness(3),
                Padding = new Thickness(10, 4, 10, 4)
            };
            button.Click += Banner_Click;
            BannerButtonsPanel.Children.Add(button);
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
    private void ChooseBanner_Click(object sender, RoutedEventArgs e) => ChooseCustomBannerRequested?.Invoke(this, EventArgs.Empty);

    private void Avatar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string avatarKey })
        {
            _selectedAvatarKey = ProfileAvatarCatalog.Normalize(avatarKey);
            _customAvatarSourcePath = null;
            UpdateAvatarPresentation();
        }
    }

    private void Banner_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string bannerKey })
        {
            _selectedBannerKey = ProfileBannerCatalog.Normalize(bannerKey);
            _customBannerSourcePath = null;
            UpdateBannerPresentation();
        }
    }

    private void Showcase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string modeName } &&
            Enum.TryParse<ProfileShowcaseMode>(modeName, true, out var mode))
        {
            _selectedShowcaseMode = mode;
            UpdateShowcasePresentation();
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
            var source = TryLoadPendingImage(_customAvatarSourcePath) ?? (_profile is null ? null : ProfileAvatarCatalog.TryLoadCustomImage(_profile));
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

    private void UpdateBannerPresentation()
    {
        var normalized = ProfileBannerCatalog.Normalize(_selectedBannerKey);
        BannerPreviewGrid.Background = ProfileBannerCatalog.CreateBrush(normalized);
        BannerPreviewImage.Source = null;
        BannerPreviewImage.Visibility = Visibility.Collapsed;

        if (string.Equals(normalized, ProfileBannerCatalog.CustomKey, StringComparison.OrdinalIgnoreCase))
        {
            var source = TryLoadPendingImage(_customBannerSourcePath)
                         ?? (_profile is null ? null : ProfileBannerCatalog.TryLoadCustomImage(_profile.GrevId, _presentation));
            if (source is not null)
            {
                BannerPreviewImage.Source = source;
                BannerPreviewImage.Visibility = Visibility.Visible;
            }
            BannerChoiceText.Text = "Custom banner";
        }
        else
        {
            BannerChoiceText.Text = ProfileBannerCatalog.Presets.First(item => item.Key == normalized).Name;
        }

        foreach (var button in BannerButtonsPanel.Children.OfType<Button>())
        {
            if (button.Tag is not string key) continue;
            var preset = ProfileBannerCatalog.Presets.First(item => item.Key == key);
            button.Content = string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)
                ? $"✓ {preset.Name}"
                : preset.Name;
        }
    }

    private void UpdateShowcasePresentation()
    {
        TopPlayedShowcaseButton.Content = _selectedShowcaseMode == ProfileShowcaseMode.TopPlayed ? "✓ Top Played" : "Top Played";
        RecentShowcaseButton.Content = _selectedShowcaseMode == ProfileShowcaseMode.RecentActivity ? "✓ Recent Activity" : "Recent Activity";
        MilestonesShowcaseButton.Content = _selectedShowcaseMode == ProfileShowcaseMode.Milestones ? "✓ Milestones" : "Milestones";
    }

    private static BitmapImage? TryLoadPendingImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
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
