using System.Windows;
using System.Windows.Controls;
using GrevHome.Input;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record ShortcutRecordRequest(ControllerShortcutAction Action, string? ExistingBindingId);
public sealed record ShortcutHoldAdjustment(string BindingId, int DeltaMilliseconds);

public partial class SettingsView : UserControl
{
    private LocalProfile? _profile;
    private ControllerShortcutConfiguration _shortcuts = ControllerShortcutService.CreateDefaults();

    public event EventHandler? BackRequested;
    public event Action<string>? SaveDisplayNameRequested;
    public event Action<ShortcutRecordRequest>? RecordShortcutRequested;
    public event Action<string>? RemoveShortcutRequested;
    public event Action<ShortcutHoldAdjustment>? AdjustShortcutHoldRequested;
    public event EventHandler? ResetShortcutsRequested;
    public event EventHandler? CancelShortcutCaptureRequested;

    public SettingsView()
    {
        InitializeComponent();
        BuildDisplayNameKeyboard();
    }

    public void SetState(LocalProfile? profile, ControllerShortcutConfiguration shortcuts)
    {
        _profile = profile;
        _shortcuts = shortcuts;

        if (profile is null)
        {
            DisplayNameText.Text = "Guest / online session";
            UsernameText.Text = "No editable local Username in this session";
            GrevIdText.Text = "Local account settings require a local Primary User";
            EditDisplayNameButton.IsEnabled = false;
            DisplayNameEditor.Visibility = Visibility.Collapsed;
            AccountStatusText.Text = "Switch Primary User to a local account to edit its Display Name.";
        }
        else
        {
            DisplayNameText.Text = profile.DisplayName;
            UsernameText.Text = $"Username: @{profile.Username}  •  permanent";
            GrevIdText.Text = $"GrevID: {profile.GrevId}  •  permanent";
            EditDisplayNameButton.IsEnabled = true;
            AccountStatusText.Text = "Display Name is cosmetic. Username and GrevID are not changed here.";
        }

        RenderShortcuts();
    }

    public void ShowAccountStatus(string message, bool closeEditor = false)
    {
        AccountStatusText.Text = message;
        if (closeEditor)
        {
            DisplayNameEditor.Visibility = Visibility.Collapsed;
        }
    }

    public void ShowShortcutStatus(string message)
    {
        ShortcutStatusText.Text = message;
    }

    public void BeginCapture(ControllerShortcutAction action, bool replacing)
    {
        CapturePanel.Visibility = Visibility.Visible;
        CaptureTitleText.Text = replacing
            ? $"Re-recording {FormatAction(action)}…"
            : $"Recording new {FormatAction(action)} combination…";
        CaptureInstructionsText.Text = "Release the button used to start recording, then hold every button you want in the combination together. Release the combination to save it.";
        ShortcutStatusText.Text = "Recording is active. It will cancel automatically after 15 seconds if no combination is completed.";
    }

    public void EndCapture(string message)
    {
        CapturePanel.Visibility = Visibility.Collapsed;
        ShortcutStatusText.Text = message;
    }

    private void RenderShortcuts()
    {
        ReturnHomeBindingsPanel.Children.Clear();
        OverlayBindingsPanel.Children.Clear();

        foreach (var binding in _shortcuts.Bindings.Where(binding => binding.Enabled))
        {
            var target = binding.Action == ControllerShortcutAction.ReturnHome
                ? ReturnHomeBindingsPanel
                : OverlayBindingsPanel;
            target.Children.Add(CreateBindingRow(binding));
        }

        if (OverlayBindingsPanel.Children.Count == 0)
        {
            OverlayBindingsPanel.Children.Add(CreateEmptyLabel("No Overlay shortcut configured."));
        }
    }

    private UIElement CreateBindingRow(ControllerShortcutBinding binding)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 14, 21)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 61, 81)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = FormatButtons(binding.Buttons),
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = $"Hold {binding.HoldMilliseconds} ms  •  {binding.Id}",
                        Margin = new Thickness(0, 5, 0, 0),
                        Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
                    }
                }
            }
        };
        grid.Children.Add(details);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);

        actions.Children.Add(CreateActionButton("Re-record", binding.Id, (_, _) =>
            RecordShortcutRequested?.Invoke(new ShortcutRecordRequest(binding.Action, binding.Id)), 110));
        actions.Children.Add(CreateActionButton("-100ms", binding.Id, (_, _) =>
            AdjustShortcutHoldRequested?.Invoke(new ShortcutHoldAdjustment(binding.Id, -100)), 92));
        actions.Children.Add(CreateActionButton("+100ms", binding.Id, (_, _) =>
            AdjustShortcutHoldRequested?.Invoke(new ShortcutHoldAdjustment(binding.Id, 100)), 92));
        actions.Children.Add(CreateActionButton("Remove", binding.Id, (_, _) =>
            RemoveShortcutRequested?.Invoke(binding.Id), 90));

        grid.Children.Add(actions);
        return grid;
    }

    private Button CreateActionButton(string text, string tag, RoutedEventHandler handler, double width)
    {
        var button = new Button
        {
            Content = text,
            Tag = tag,
            Width = width,
            Height = 44,
            Margin = new Thickness(4, 0, 0, 0)
        };
        button.Click += handler;
        return button;
    }

    private TextBlock CreateEmptyLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(2, 4, 0, 8),
        Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
    };

    private void BuildDisplayNameKeyboard()
    {
        const string keys = "QWERTYUIOPASDFGHJKLZXCVBNM1234567890";
        foreach (var key in keys)
        {
            var button = new Button
            {
                Content = key.ToString(),
                Tag = key,
                Height = 48,
                Margin = new Thickness(3),
                FontSize = 17
            };
            button.Click += DisplayNameKey_Click;
            DisplayNameKeyboard.Children.Add(button);
        }
    }

    private void EditDisplayName_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            return;
        }

        DisplayNameTextBox.Text = _profile.DisplayName;
        DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
        DisplayNameEditor.Visibility = Visibility.Visible;
    }

    private void DisplayNameKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: char key } && DisplayNameTextBox.Text.Length < DisplayNameTextBox.MaxLength)
        {
            DisplayNameTextBox.Text += key;
            DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
        }
    }

    private void DisplayNameSpace_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayNameTextBox.Text.Length < DisplayNameTextBox.MaxLength)
        {
            DisplayNameTextBox.Text += " ";
            DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
        }
    }

    private void DisplayNameBackspace_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayNameTextBox.Text.Length == 0)
        {
            return;
        }

        DisplayNameTextBox.Text = DisplayNameTextBox.Text[..^1];
        DisplayNameTextBox.CaretIndex = DisplayNameTextBox.Text.Length;
    }

    private void SaveDisplayName_Click(object sender, RoutedEventArgs e) =>
        SaveDisplayNameRequested?.Invoke(DisplayNameTextBox.Text);

    private void CancelDisplayName_Click(object sender, RoutedEventArgs e) =>
        DisplayNameEditor.Visibility = Visibility.Collapsed;

    private void AddReturnHome_Click(object sender, RoutedEventArgs e) =>
        RecordShortcutRequested?.Invoke(new ShortcutRecordRequest(ControllerShortcutAction.ReturnHome, null));

    private void AddOverlay_Click(object sender, RoutedEventArgs e) =>
        RecordShortcutRequested?.Invoke(new ShortcutRecordRequest(ControllerShortcutAction.Overlay, null));

    private void CancelCapture_Click(object sender, RoutedEventArgs e) =>
        CancelShortcutCaptureRequested?.Invoke(this, EventArgs.Empty);

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e) =>
        ResetShortcutsRequested?.Invoke(this, EventArgs.Empty);

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private static string FormatAction(ControllerShortcutAction action) =>
        action == ControllerShortcutAction.ReturnHome ? "Return Home" : "Grev Overlay";

    public static string FormatButtons(IEnumerable<ControllerButton> buttons) =>
        string.Join(" + ", buttons.Select(button => button switch
        {
            ControllerButton.LeftShoulder => "LB",
            ControllerButton.RightShoulder => "RB",
            ControllerButton.LeftTrigger => "LT",
            ControllerButton.RightTrigger => "RT",
            ControllerButton.LeftThumb => "L3",
            ControllerButton.RightThumb => "R3",
            ControllerButton.View => "View",
            ControllerButton.Menu => "Menu",
            ControllerButton.DPadUp => "D-pad Up",
            ControllerButton.DPadDown => "D-pad Down",
            ControllerButton.DPadLeft => "D-pad Left",
            ControllerButton.DPadRight => "D-pad Right",
            _ => button.ToString()
        }));
}
