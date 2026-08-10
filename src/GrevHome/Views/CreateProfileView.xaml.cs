using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record CreateProfileRequest(string Username, AccountRole Role);

public partial class CreateProfileView : UserControl
{
    public event Action<CreateProfileRequest>? CreateRequested;
    public event EventHandler? CancelRequested;

    private AccountRole _selectedRole = AccountRole.Admin;

    public CreateProfileView()
    {
        InitializeComponent();
        BuildKeyboard();
        UpdateRolePresentation();
    }

    public void Reset()
    {
        ProfileNameTextBox.Clear();
        _selectedRole = AccountRole.Admin;
        UpdateRolePresentation();
        StatusText.Text = "Display Name starts the same as Username and can be changed later without changing GrevID or folders.";
    }

    public void ShowError(string message)
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
                Height = 52,
                Margin = new Thickness(4),
                FontSize = 18
            };
            button.Click += KeyboardKey_Click;
            KeyboardGrid.Children.Add(button);
        }
    }

    private void KeyboardKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: char key } && ProfileNameTextBox.Text.Length < ProfileNameTextBox.MaxLength)
        {
            ProfileNameTextBox.Text += key;
            ProfileNameTextBox.CaretIndex = ProfileNameTextBox.Text.Length;
        }
    }

    private void Space_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileNameTextBox.Text.Length < ProfileNameTextBox.MaxLength)
        {
            ProfileNameTextBox.Text += " ";
            ProfileNameTextBox.CaretIndex = ProfileNameTextBox.Text.Length;
        }
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileNameTextBox.Text.Length == 0)
        {
            return;
        }

        ProfileNameTextBox.Text = ProfileNameTextBox.Text[..^1];
        ProfileNameTextBox.CaretIndex = ProfileNameTextBox.Text.Length;
    }

    private void Role_Click(object sender, RoutedEventArgs e)
    {
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
        RoleDescriptionText.Text = _selectedRole switch
        {
            AccountRole.Admin => "Admin • full Grev Home access.",
            AccountRole.Standard => "Standard • normal player access; administrative machine controls can be restricted.",
            AccountRole.Guest => "Guest • restricted player account intended for minimal permissions and shared guest resources.",
            _ => _selectedRole.ToString()
        };

        AdminRoleButton.Content = _selectedRole == AccountRole.Admin ? "✓ Admin" : "Admin";
        StandardRoleButton.Content = _selectedRole == AccountRole.Standard ? "✓ Standard" : "Standard";
        GuestRoleButton.Content = _selectedRole == AccountRole.Guest ? "✓ Guest" : "Guest";
    }

    private void Create_Click(object sender, RoutedEventArgs e) =>
        CreateRequested?.Invoke(new CreateProfileRequest(ProfileNameTextBox.Text, _selectedRole));

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);
}
