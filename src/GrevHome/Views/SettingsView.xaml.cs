using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Input;
using GrevHome.Machine;
using GrevHome.Profiles;

namespace GrevHome.Views;

public sealed record ShortcutRecordRequest(ControllerShortcutAction Action, string? ExistingBindingId);
public sealed record ShortcutHoldAdjustment(string BindingId, int DeltaMilliseconds);

public partial class SettingsView : UserControl
{
    private readonly SystemStatusService _systemStatusService = new();
    private readonly ControllerHardwareService _controllerHardwareService = new();
    private readonly SystemPowerService _systemPowerService = new();
    private LocalProfile? _profile;
    private ControllerShortcutConfiguration _shortcuts = ControllerShortcutService.CreateDefaults();
    private SystemPowerAction? _pendingPowerAction;
    private DateTimeOffset _pendingPowerExpiresAt;

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
        RefreshSystemStatus();
        ResetPowerConfirmation();
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

    private void RefreshSystemStatus()
    {
        try
        {
            var machine = _systemStatusService.GetMachineStatus();
            MachineNameText.Text = machine.MachineName;
            WindowsText.Text = $"{machine.WindowsDescription}  •  {machine.Architecture}";
            MachineResourcesText.Text =
                $"{machine.LogicalProcessors} logical processors  •  RAM {FormatBytes(machine.AvailableMemoryBytes)} free / {FormatBytes(machine.TotalMemoryBytes)} total  •  Uptime {FormatUptime(machine.Uptime)}";
            MachinePowerText.Text = machine.BatteryPercent.HasValue
                ? $"Power: {machine.PowerSource}  •  system battery {machine.BatteryPercent}%"
                : $"Power: {machine.PowerSource}";

            RenderStorage(_systemStatusService.GetStorageStatus());
            RenderControllerHardware(_controllerHardwareService.GetControllers());
            SystemStatusText.Text = $"Status refreshed {DateTime.Now:T}. Drive and controller hardware can change while Grev Home is running.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            SystemStatusText.Text = $"Windows status refresh failed: {ex.Message}";
        }
    }

    private void RenderStorage(IReadOnlyList<StorageStatus> drives)
    {
        StorageStatusPanel.Children.Clear();

        if (drives.Count == 0)
        {
            StorageStatusPanel.Children.Add(CreateEmptyLabel("No ready drives reported by Windows."));
            return;
        }

        foreach (var drive in drives)
        {
            var used = Math.Max(0, drive.TotalBytes - drive.FreeBytes);
            var usedPercent = drive.TotalBytes <= 0 ? 0 : (int)Math.Round(used * 100d / drive.TotalBytes);
            StorageStatusPanel.Children.Add(CreateStatusRow(
                $"{drive.Name}  {drive.Label}",
                $"{drive.DriveType}  •  {drive.Format}  •  {FormatBytes(drive.FreeBytes)} free / {FormatBytes(drive.TotalBytes)}  •  {usedPercent}% used"));
        }
    }

    private void RenderControllerHardware(IReadOnlyList<ControllerHardwareStatus> controllers)
    {
        ControllerHardwarePanel.Children.Clear();

        foreach (var controller in controllers)
        {
            ControllerHardwarePanel.Children.Add(CreateStatusRow(
                $"Controller {controller.ControllerIndex + 1}",
                controller.IsConnected
                    ? $"Connected  •  {controller.BatteryType}  •  Battery {controller.BatteryLevel}"
                    : "Not connected"));
        }
    }

    private UIElement CreateStatusRow(string title, string detail)
    {
        return new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 14, 21)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 61, 81)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 17,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = detail,
                        Margin = new Thickness(0, 5, 0, 0),
                        Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private void ArmOrExecutePower(SystemPowerAction action)
    {
        var now = DateTimeOffset.UtcNow;
        if (_pendingPowerAction != action || now > _pendingPowerExpiresAt)
        {
            _pendingPowerAction = action;
            _pendingPowerExpiresAt = now.AddSeconds(8);
            UpdatePowerButtons();
            PowerStatusText.Text = $"{FormatPowerAction(action)} armed. Select the same action again within 8 seconds to confirm.";
            return;
        }

        ResetPowerConfirmation();
        try
        {
            PowerStatusText.Text = $"Requesting {FormatPowerAction(action).ToLowerInvariant()} from Windows…";
            _systemPowerService.Execute(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            PowerStatusText.Text = $"Windows did not complete the power action: {ex.Message}";
        }
    }

    private void ResetPowerConfirmation()
    {
        _pendingPowerAction = null;
        _pendingPowerExpiresAt = DateTimeOffset.MinValue;
        UpdatePowerButtons();
        PowerStatusText.Text = "No power action armed.";
    }

    private void UpdatePowerButtons()
    {
        SleepButton.Content = _pendingPowerAction == SystemPowerAction.Sleep ? "CONFIRM SLEEP" : "Sleep";
        RestartButton.Content = _pendingPowerAction == SystemPowerAction.Restart ? "CONFIRM RESTART" : "Restart";
        ShutdownButton.Content = _pendingPowerAction == SystemPowerAction.Shutdown ? "CONFIRM SHUT DOWN" : "Shut Down";
    }

    private static string FormatBytes(ulong bytes) =>
        FormatBytes(bytes > long.MaxValue ? long.MaxValue : (long)bytes);

    private static string FormatBytes(long bytes)
    {
        const double gibibyte = 1024d * 1024d * 1024d;
        const double mebibyte = 1024d * 1024d;

        return bytes >= gibibyte
            ? $"{bytes / gibibyte:0.0} GB"
            : $"{bytes / mebibyte:0.0} MB";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }

        return $"{uptime.Hours}h {uptime.Minutes}m";
    }

    private static string FormatPowerAction(SystemPowerAction action) => action switch
    {
        SystemPowerAction.Shutdown => "Shut Down",
        SystemPowerAction.Restart => "Restart",
        SystemPowerAction.Sleep => "Sleep",
        _ => action.ToString()
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

    private void RefreshSystemStatus_Click(object sender, RoutedEventArgs e) =>
        RefreshSystemStatus();

    private void Sleep_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecutePower(SystemPowerAction.Sleep);

    private void Restart_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecutePower(SystemPowerAction.Restart);

    private void Shutdown_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecutePower(SystemPowerAction.Shutdown);

    private void CancelPowerAction_Click(object sender, RoutedEventArgs e) =>
        ResetPowerConfirmation();

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
