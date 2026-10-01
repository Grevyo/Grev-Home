using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;
using GrevHome.Online;

namespace GrevHome.Views;

public sealed record CreateProfileRequest(string Username, AccountRole Role, bool LinkGrevDad = false);

public partial class CreateProfileView : UserControl
{
    public event Action<CreateProfileRequest>? CreateRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? KeyboardOpened;
    public event EventHandler? KeyboardClosed;
    public event Action<LocalProfile>? GenerateGrevDadCodeRequested;
    public event Action<LocalProfile, GrevDadLinkStart>? OpenGrevDadApprovalRequested;
    public event Action<LocalProfile>? OnboardingFinished;
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
            Dispatcher.BeginInvoke(new Action(() => CreateAccountButton.Focus()));
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

    /// <summary>
    /// Shows the optional Grev.dad step for a just-created local account. With
    /// <paramref name="startLinking"/> the approval code is requested straight away, so the
    /// "Create + link" path is a single choice rather than a separate generate step.
    /// </summary>
    public void ShowGrevDadStep(LocalProfile profile, bool startLinking)
    {
        _createdProfile = profile;
        _grevDadLink = null;
        AccountDetailsStep.Visibility = Visibility.Collapsed;
        GrevDadLinkStep.Visibility = Visibility.Visible;
        GrevDadCodeText.Text = string.Empty;
        GrevDadInstructionsText.Visibility = Visibility.Collapsed;
        OpenGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        NewGrevDadCodeButton.Visibility = startLinking ? Visibility.Collapsed : Visibility.Visible;
        NewGrevDadCodeButton.Content = "Get approval code";
        FinishGrevDadButton.Visibility = Visibility.Collapsed;
        SkipGrevDadButton.Visibility = Visibility.Visible;
        GrevDadOnboardingStatus.Text = $"{profile.DisplayName} is ready to use offline. Linking is optional.";
        if (startLinking)
        {
            Dispatcher.BeginInvoke(new Action(() => GenerateGrevDadCodeRequested?.Invoke(profile)));
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => NewGrevDadCodeButton.Focus()));
        }
    }

    public void ShowGrevDadWorking(string message)
    {
        OpenGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        NewGrevDadCodeButton.Visibility = Visibility.Collapsed;
        GrevDadOnboardingStatus.Text = message;
        SkipGrevDadButton.Focus();
    }

    public void ShowGrevDadCode(GrevDadLinkStart link, string siteHost)
    {
        _grevDadLink = link;
        GrevDadCodeText.Text = link.UserCode;
        GrevDadInstructionsText.Text =
            $"On this PC: choose Approve in Grev Home browser and sign in to Grev.dad if asked.\n" +
            $"On a phone or another PC: open {siteHost}/link-grev-home, sign in and enter the code above.";
        GrevDadInstructionsText.Visibility = Visibility.Visible;
        GrevDadOnboardingStatus.Text = $"Waiting for approval… this code expires at {link.ExpiresAtUtc.ToLocalTime():t}.";
        OpenGrevDadApprovalButton.Visibility = Visibility.Visible;
        NewGrevDadCodeButton.Visibility = Visibility.Collapsed;
        OpenGrevDadApprovalButton.Focus();
    }

    /// <summary>The current code can no longer be approved (expired, denied, unreachable). Offers a fresh one.</summary>
    public void ShowGrevDadRetry(string message, string retryLabel = "Get a new code")
    {
        _grevDadLink = null;
        GrevDadCodeText.Text = string.Empty;
        GrevDadInstructionsText.Visibility = Visibility.Collapsed;
        OpenGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        NewGrevDadCodeButton.Content = retryLabel;
        NewGrevDadCodeButton.Visibility = Visibility.Visible;
        GrevDadOnboardingStatus.Text = message;
        NewGrevDadCodeButton.Focus();
    }

    public void ShowGrevDadOnboardingStatus(string message) => GrevDadOnboardingStatus.Text = message;

    public void ShowGrevDadLinked(string accountName)
    {
        _grevDadLink = null;
        GrevDadCodeText.Text = "Connected";
        GrevDadInstructionsText.Text = $"This account is now linked to {accountName}.";
        GrevDadInstructionsText.Visibility = Visibility.Visible;
        GrevDadOnboardingStatus.Text = "Restoring your Grev.dad data…";
        OpenGrevDadApprovalButton.Visibility = Visibility.Collapsed;
        NewGrevDadCodeButton.Visibility = Visibility.Collapsed;
        SkipGrevDadButton.Visibility = Visibility.Collapsed;
        FinishGrevDadButton.Visibility = Visibility.Visible;
        FinishGrevDadButton.Focus();
    }

    /// <summary>True while this view is showing the link step for <paramref name="grevId"/>.</summary>
    public bool IsOnboardingLinkFor(string grevId) =>
        GrevDadLinkStep.Visibility == Visibility.Visible &&
        string.Equals(_createdProfile?.GrevId, grevId, StringComparison.OrdinalIgnoreCase);

    public void CancelKeyboard() => KeyboardOverlay.Cancel();

    public void RestoreControllerFocusAfterKeyboard()
    {
        Dispatcher.BeginInvoke(new Action(() =>
            (string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? OpenKeyboardButton : CreateAccountButton).Focus()));
    }

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

        // Guest accounts are temporary-feeling by design and do not get the Grev.dad shortcut.
        CreateLinkedAccountButton.Visibility = _selectedRole == AccountRole.Guest && !_firstProfile
            ? Visibility.Collapsed
            : Visibility.Visible;

        AdminRoleButton.Content = _selectedRole == AccountRole.Admin ? "✓ Admin" : "Admin";
        StandardRoleButton.Content = _selectedRole == AccountRole.Standard ? "✓ Standard" : "Standard";
        GuestRoleButton.Content = _selectedRole == AccountRole.Guest ? "✓ Guest" : "Guest";
    }

    private void Create_Click(object sender, RoutedEventArgs e) => RequestCreate(linkGrevDad: false);

    private void CreateLinked_Click(object sender, RoutedEventArgs e) => RequestCreate(linkGrevDad: true);

    private void RequestCreate(bool linkGrevDad) =>
        CreateRequested?.Invoke(new CreateProfileRequest(
            ProfileNameTextBox.Text,
            _firstProfile ? AccountRole.Admin : _selectedRole,
            linkGrevDad && (_firstProfile || _selectedRole != AccountRole.Guest)));

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void NewGrevDadCode_Click(object sender, RoutedEventArgs e) { if (_createdProfile is { } profile) GenerateGrevDadCodeRequested?.Invoke(profile); }
    private void OpenGrevDadApproval_Click(object sender, RoutedEventArgs e) { if (_createdProfile is { } profile && _grevDadLink is { } link) OpenGrevDadApprovalRequested?.Invoke(profile, link); }
    private void SkipGrevDad_Click(object sender, RoutedEventArgs e) { if (_createdProfile is { } profile) OnboardingSkipped?.Invoke(profile); }
    private void FinishGrevDad_Click(object sender, RoutedEventArgs e) { if (_createdProfile is { } profile) OnboardingFinished?.Invoke(profile); }
}
