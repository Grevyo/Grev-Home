using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record CreateProfileRequest(string Username, AccountRole Role);

public partial class CreateProfileView : UserControl
{
    public event Action<CreateProfileRequest>? CreateRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? KeyboardOpened;
    public event EventHandler? KeyboardClosed;

    private AccountRole _selectedRole = AccountRole.Admin;
    private bool _firstProfile;
    private bool _openKeyboardWhenLoaded;

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
}
