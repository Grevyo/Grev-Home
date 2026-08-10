using System.Windows;
using System.Windows.Controls;

namespace GrevHome.Views;

public partial class CreateProfileView : UserControl
{
    public event Action<string>? CreateRequested;
    public event EventHandler? CancelRequested;

    public CreateProfileView()
    {
        InitializeComponent();
        BuildKeyboard();
    }

    public void Reset()
    {
        ProfileNameTextBox.Clear();
        StatusText.Text = "Use the controller keyboard below, or type with a physical keyboard. A permanent GrevID is created with the account.";
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

    private void Create_Click(object sender, RoutedEventArgs e) =>
        CreateRequested?.Invoke(ProfileNameTextBox.Text);

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);
}
