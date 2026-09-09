using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;
using GrevHome.Online;
using System.Windows.Media;

namespace GrevHome.Views;

public sealed record CreateProfileRequest(string Username, AccountRole Role);

public partial class CreateProfileView : UserControl
{
    public event Action<CreateProfileRequest>? CreateRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? KeyboardOpened;
    public event EventHandler? KeyboardClosed;
    public event Action<LocalProfile>? OpenGrevDadRequested;
    public event Action<LocalProfile>? GenerateGrevDadCodeRequested;
    public event Action<LocalProfile, GrevDadLinkStart>? OpenGrevDadApprovalRequested;
    public event Action<LocalProfile>? CheckGrevDadApprovalRequested;
    public event EventHandler? OnboardingFinished;
    public event Action<LocalProfile>? OnboardingSkipped;

    private AccountRole _selectedRole = AccountRole.Admin;
    private bool _firstProfile;
    private bool _openKeyboardWhenLoaded;
    private LocalProfile? _createdProfile;
    private GrevDadLinkStart? _grevDadLink;

    public bool IsKeyboardOpen => KeyboardOverlay.IsOpen;

    public CreateProfileView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += value =>
        {
            ProfileNameTextBox.Text = value;
            ProfileNameTextBox.CaretIndex = ProfileNameTextBox.Text.Length;
        };
        KeyboardOverlay.Opened += (_, _) => KeyboardOpened?.Invoke(this, EventArgs.Empty);
        KeyboardOverlay.Closed += (_, _) => KeyboardClosed?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) =>
        {
            if (_openKeyboardWhenLoaded)
            {
                _openKeyboardWhenLoaded = false;
                OpenKeyboard();
            }
        };
        UpdateRolePresentation();
    }

    public void Reset(bool firstProfile = false)
    {
        _firstProfile = firstProfile;
        _createdProfile = null;
        _grevDadLink = null;
        AccountDetailsStep.Visibility = Visibility.Visible;
        GrevDadLinkStep.Visibility = Visibility.Collapsed;
        ProfileNameTextBox.Clear();
        _selectedRole = AccountRole.Admin;
        AdminRoleButton.IsEnabled = true;
        StandardRoleButton.IsEnabled = !firstProfile;
        GuestRoleButton.IsEnabled = !firstProfile;
        UpdateRolePresentation();
        StatusText.Text = firstProfile
            ? "The first Grev Home account is always an Admin. Display Name starts the same as Username and can be changed later."
            : "Display Name starts the same as Username and can be changed later without changing GrevID or folders.";
        _openKeyboardWhenLoaded = true;
    }

    public void ShowError(string message) => StatusText.Text = message;

    public void ShowGrevDadStep(LocalProfile profile)
    {
        _createdProfile = profile;
        AccountDetailsStep.Visibility = Visibility.Collapsed;
        GrevDadLinkStep.Visibility = Visibility.Visible;
        GrevDadCodeText.Text = string.Empty;
        GrevDadOnboardingStatus.Text = $"{profile.DisplayName} has been created locally. Linking is optional and can also be done later from Edit Profile.";
        GenerateGrevDadCodeButton.Visibility = Visibility.Visible;
        OpenGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        CheckGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        FinishGrevDadButton.Visibility = Visibility.Collapsed;
        SkipGrevDadButton.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(()=>OpenGrevDadButton.Focus()));
    }

    public void ShowGrevDadCode(GrevDadLinkStart link)
    {
        _grevDadLink = link;
        GrevDadCodeText.Text = $"Approval code: {link.UserCode}";
        GrevDadOnboardingStatus.Text = "Approve this code on the signed-in Grev.dad account, then choose Check approval.";
        GenerateGrevDadCodeButton.Visibility = Visibility.Collapsed;
        OpenGrevDadApprovalButton.Visibility = Visibility.Visible;
        CheckGrevDadApprovalButton.Visibility = Visibility.Visible;
        var green = new SolidColorBrush(Color.FromRgb(24,105,57));
        OpenGrevDadApprovalButton.Background = green;
        CheckGrevDadApprovalButton.Background = green;
        CheckGrevDadApprovalButton.Focus();
    }

    public void ShowGrevDadOnboardingStatus(string message) => GrevDadOnboardingStatus.Text = message;

    public void ShowGrevDadLinked(string accountName)
    {
        GrevDadCodeText.Text = "Connected";
        GrevDadOnboardingStatus.Text = $"Linked to {accountName}. Shared account data is now being downloaded.";
        GenerateGrevDadCodeButton.Visibility = Visibility.Collapsed;
        OpenGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        CheckGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        SkipGrevDadButton.Visibility = Visibility.Collapsed;
        FinishGrevDadButton.Visibility = Visibility.Visible;
        FinishGrevDadButton.Focus();
    }

    public void CancelKeyboard() => KeyboardOverlay.Cancel();

    private void OpenKeyboard_Click(object sender, RoutedEventArgs e) => OpenKeyboard();

    private void OpenKeyboard() =>
        KeyboardOverlay.Open("Enter Username", ProfileNameTextBox.Text, ProfileNameTextBox.MaxLength);

    private void Role_Click(object sender, RoutedEventArgs e)
    {
        if (_firstProfile)
        {
            _selectedRole = AccountRole.Admin;
            UpdateRolePresentation();
            return;
        }

        if (sender is not Button { Tag: string roleName } ||
            !Enum.TryParse<AccountRole>(roleName, ignoreCase: true, out var role))
        {
            return;
        }

        _selectedRole = role;
        UpdateRolePresentation();
    }

    private void UpdateRolePresentation()
    {
        RoleDescriptionText.Text = _firstProfile
            ? "Admin • the first account owns initial Grev Home administration."
            : AccountAuthorizationService.DescribeRole(_selectedRole);

        AdminRoleButton.Content = _selectedRole == AccountRole.Admin ? "✓ Admin" : "Admin";
        StandardRoleButton.Content = _selectedRole == AccountRole.Standard ? "✓ Standard" : "Standard";
        GuestRoleButton.Content = _selectedRole == AccountRole.Guest ? "✓ Guest" : "Guest";
    }

    private void Create_Click(object sender, RoutedEventArgs e) =>
        CreateRequested?.Invoke(new CreateProfileRequest(
            ProfileNameTextBox.Text,
            _firstProfile ? AccountRole.Admin : _selectedRole));

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OpenGrevDad_Click(object sender, RoutedEventArgs e) { if(_createdProfile is { } profile) OpenGrevDadRequested?.Invoke(profile); }
    private void GenerateGrevDadCode_Click(object sender, RoutedEventArgs e) { if(_createdProfile is { } profile) GenerateGrevDadCodeRequested?.Invoke(profile); }
    private void OpenGrevDadApproval_Click(object sender, RoutedEventArgs e) { if(_createdProfile is { } profile && _grevDadLink is { } link) OpenGrevDadApprovalRequested?.Invoke(profile,link); }
    private void CheckGrevDadApproval_Click(object sender, RoutedEventArgs e) { if(_createdProfile is { } profile) CheckGrevDadApprovalRequested?.Invoke(profile); }
    private void SkipGrevDad_Click(object sender, RoutedEventArgs e)
    {
        if (_createdProfile is { } profile) OnboardingSkipped?.Invoke(profile);
        else OnboardingFinished?.Invoke(this,EventArgs.Empty);
    }
    private void FinishGrevDad_Click(object sender, RoutedEventArgs e) => OnboardingFinished?.Invoke(this,EventArgs.Empty);
}
