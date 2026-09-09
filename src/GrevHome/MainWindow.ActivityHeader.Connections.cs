using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Machine;

namespace GrevHome;

public partial class MainWindow
{
    private Border? _activityWifiFlyout;
    private TextBlock? _activityWifiStatusText;
    private StackPanel? _activityWifiNetworksPanel;
    private Button? _activityWifiRefreshButton;
    private Button? _activityWifiDisconnectButton;

    private Border? _activityBluetoothFlyout;
    private TextBlock? _activityBluetoothStatusText;
    private StackPanel? _activityBluetoothDevicesPanel;
    private Button? _activityBluetoothToggleButton;
    private Button? _activityBluetoothRefreshButton;

    private void BuildConnectionQuickControls()
    {
        if (PowerMenuOverlay.Child is not Grid overlayGrid)
        {
            return;
        }

        BuildWifiQuickControl(overlayGrid);
        BuildBluetoothQuickControl(overlayGrid);
    }

    private void BuildWifiQuickControl(Grid overlayGrid)
    {
        if (_activityWifiFlyout is not null) return;

        _activityWifiFlyout = CreateConnectionFlyout(440);
        var content = new StackPanel();
        content.Children.Add(CreateQuickHeading("WI-FI"));

        _activityWifiStatusText = CreateQuickStatusText();
        content.Children.Add(_activityWifiStatusText);

        var actions = new Grid { Margin = new Thickness(0, 12, 0, 8) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        _activityWifiRefreshButton = CreateQuickActionButton("Refresh", (_, _) => _ = RefreshWifiQuickControlAsync());
        _activityWifiDisconnectButton = CreateQuickActionButton("Disconnect", (_, _) => _ = DisconnectWifiQuickAsync());
        Grid.SetColumn(_activityWifiDisconnectButton, 1);
        actions.Children.Add(_activityWifiRefreshButton);
        actions.Children.Add(_activityWifiDisconnectButton);
        content.Children.Add(actions);

        content.Children.Add(new TextBlock
        {
            Text = "AVAILABLE NETWORKS",
            Margin = new Thickness(0, 8, 0, 4),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedBrush")
        });

        _activityWifiNetworksPanel = new StackPanel();
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 330,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _activityWifiNetworksPanel
        });

        content.Children.Add(CreateQuickCloseButton());
        _activityWifiFlyout.Child = content;
        overlayGrid.Children.Add(_activityWifiFlyout);
    }

    private void BuildBluetoothQuickControl(Grid overlayGrid)
    {
        if (_activityBluetoothFlyout is not null) return;

        _activityBluetoothFlyout = CreateConnectionFlyout(440);
        var content = new StackPanel();
        content.Children.Add(CreateQuickHeading("BLUETOOTH"));

        _activityBluetoothStatusText = CreateQuickStatusText();
        content.Children.Add(_activityBluetoothStatusText);

        var actions = new Grid { Margin = new Thickness(0, 12, 0, 8) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        _activityBluetoothToggleButton = CreateQuickActionButton("Bluetooth", (_, _) => _ = ToggleBluetoothQuickAsync());
        _activityBluetoothRefreshButton = CreateQuickActionButton("Refresh", (_, _) => _ = RefreshBluetoothQuickControlAsync());
        Grid.SetColumn(_activityBluetoothRefreshButton, 1);
        actions.Children.Add(_activityBluetoothToggleButton);
        actions.Children.Add(_activityBluetoothRefreshButton);
        content.Children.Add(actions);

        content.Children.Add(new TextBlock
        {
            Text = "DEVICES",
            Margin = new Thickness(0, 8, 0, 4),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedBrush")
        });

        _activityBluetoothDevicesPanel = new StackPanel();
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 330,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _activityBluetoothDevicesPanel
        });

        content.Children.Add(CreateQuickCloseButton());
        _activityBluetoothFlyout.Child = content;
        overlayGrid.Children.Add(_activityBluetoothFlyout);
    }

    private Border CreateConnectionFlyout(double width)
    {
        var flyout = new Border
        {
            Width = width,
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 70, 95)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(flyout, 120);
        return flyout;
    }

    private TextBlock CreateQuickHeading(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = (Brush)FindResource("AccentBrush")
    };

    private TextBlock CreateQuickStatusText() => new()
    {
        Text = "Reading status…",
        Margin = new Thickness(0, 8, 0, 0),
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap
    };

    private static Button CreateQuickActionButton(string content, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = content,
            MinHeight = 48,
            Margin = new Thickness(4)
        };
        button.Click += handler;
        return button;
    }

    private Button CreateQuickCloseButton()
    {
        var button = new Button
        {
            Content = "Close",
            MinHeight = 46,
            Margin = new Thickness(0, 12, 0, 0)
        };
        button.Click += (_, _) => ClosePowerMenu();
        return button;
    }

    private void OpenWifiQuickControl()
    {
        if (_activityWifiButton is null || _activityWifiFlyout is null || IsStoreModalOpen) return;
        OpenConnectionQuickControl(_activityWifiButton, _activityWifiFlyout);
        _ = RefreshWifiQuickControlAsync();
        Dispatcher.BeginInvoke(new Action(() => _activityWifiRefreshButton?.Focus()));
    }

    private void OpenBluetoothQuickControl()
    {
        if (_activityBluetoothButton is null || _activityBluetoothFlyout is null || IsStoreModalOpen) return;
        OpenConnectionQuickControl(_activityBluetoothButton, _activityBluetoothFlyout);
        _ = RefreshBluetoothQuickControlAsync();
        Dispatcher.BeginInvoke(new Action(() => _activityBluetoothToggleButton?.Focus()));
    }

    private void OpenConnectionQuickControl(Button anchorButton, Border flyout)
    {
        if (IsPowerMenuOpen)
        {
            ClosePowerMenu(returnFocusToHeader: false);
        }

        ResetHeaderPowerConfirmation();
        _headerFlyoutReturnButton = anchorButton;
        ProfileQuickMenuCard.Visibility = Visibility.Collapsed;
        PowerMenuCard.Visibility = Visibility.Collapsed;
        HideActivityQuickControls();

        var anchor = anchorButton.TranslatePoint(new Point(0, anchorButton.ActualHeight), PowerMenuOverlay);
        var left = Math.Clamp(anchor.X, 16, Math.Max(16, ActualWidth - flyout.Width - 16));
        flyout.Margin = new Thickness(left, anchor.Y + 8, 0, 0);
        flyout.Visibility = Visibility.Visible;
        ShellInteractionHost.IsEnabled = false;
        PowerMenuOverlay.Visibility = Visibility.Visible;
    }

    private void HideConnectionQuickControls()
    {
        if (_activityWifiFlyout is not null) _activityWifiFlyout.Visibility = Visibility.Collapsed;
        if (_activityBluetoothFlyout is not null) _activityBluetoothFlyout.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshWifiQuickControlAsync()
    {
        if (_activityWifiStatusText is null || _activityWifiNetworksPanel is null) return;

        try
        {
            _activityWifiStatusText.Text = "Scanning Wi-Fi…";
            var status = await Task.Run(_activityWifiService.GetStatus);
            var networks = status.AdapterAvailable
                ? await Task.Run(_activityWifiService.GetAvailableNetworks)
                : Array.Empty<WifiNetwork>();

            _activityWifiStatusText.Text = status switch
            {
                { AdapterAvailable: false } => "No Wi-Fi adapter is available.",
                { IsConnected: true } => $"Connected to {status.Ssid} • {status.SignalQuality}% signal",
                _ => "Wi-Fi is available but not connected."
            };
            if (_activityWifiDisconnectButton is not null)
            {
                _activityWifiDisconnectButton.IsEnabled = status.IsConnected;
            }

            _activityWifiNetworksPanel.Children.Clear();
            foreach (var network in networks.Take(12))
            {
                var label = network.IsConnected
                    ? $"{network.Ssid}   •   Connected   •   {network.SignalQuality}%"
                    : network.CanConnect
                        ? $"{network.Ssid}   •   {network.SignalQuality}%   •   Connect"
                        : $"{network.Ssid}   •   {network.SignalQuality}%   •   Save in Windows first";
                var button = new Button
                {
                    Content = label,
                    MinHeight = 48,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsEnabled = !network.IsConnected && network.CanConnect && !string.IsNullOrWhiteSpace(network.ProfileName)
                };
                var profileName = network.ProfileName;
                if (button.IsEnabled && profileName is not null)
                {
                    button.Click += (_, _) => _ = ConnectWifiQuickAsync(profileName);
                }
                _activityWifiNetworksPanel.Children.Add(button);
            }

            if (networks.Count == 0)
            {
                _activityWifiNetworksPanel.Children.Add(new TextBlock
                {
                    Text = "No Wi-Fi networks were reported.",
                    Margin = new Thickness(0, 8, 0, 0),
                    Foreground = (Brush)FindResource("MutedBrush")
                });
            }

            _ = RefreshActivityHeaderAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            _activityWifiStatusText.Text = $"Wi-Fi unavailable: {ex.Message}";
        }
    }

    private async Task ConnectWifiQuickAsync(string profileName)
    {
        if (_activityWifiStatusText is null) return;
        try
        {
            _activityWifiStatusText.Text = $"Connecting to {profileName}…";
            await Task.Run(() => _activityWifiService.ConnectSavedProfile(profileName));
            await Task.Delay(900);
            await RefreshWifiQuickControlAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            _activityWifiStatusText.Text = $"Could not connect: {ex.Message}";
        }
    }

    private async Task DisconnectWifiQuickAsync()
    {
        if (_activityWifiStatusText is null) return;
        try
        {
            _activityWifiStatusText.Text = "Disconnecting Wi-Fi…";
            await Task.Run(_activityWifiService.Disconnect);
            await Task.Delay(500);
            await RefreshWifiQuickControlAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            _activityWifiStatusText.Text = $"Could not disconnect: {ex.Message}";
        }
    }

    private async Task RefreshBluetoothQuickControlAsync()
    {
        if (_activityBluetoothStatusText is null || _activityBluetoothDevicesPanel is null) return;

        try
        {
            _activityBluetoothStatusText.Text = "Reading Bluetooth…";
            var status = await _activityBluetoothService.GetStatusAsync();
            _activityBluetoothStatusText.Text = !status.RadioAvailable
                ? "No Bluetooth radio is available."
                : $"Bluetooth is {(status.IsEnabled ? "on" : "off")} • {status.Devices.Count(device => device.IsPaired)} paired";

            if (_activityBluetoothToggleButton is not null)
            {
                _activityBluetoothToggleButton.IsEnabled = status.RadioAvailable;
                _activityBluetoothToggleButton.Content = status.IsEnabled ? "Turn Bluetooth off" : "Turn Bluetooth on";
                _activityBluetoothToggleButton.Tag = status.IsEnabled;
            }

            _activityBluetoothDevicesPanel.Children.Clear();
            foreach (var device in status.Devices.Take(12))
            {
                var button = new Button
                {
                    Content = device.IsPaired ? $"{device.Name}   •   Paired" : $"{device.Name}   •   Pair",
                    MinHeight = 48,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsEnabled = status.IsEnabled && !device.IsPaired
                };
                if (!device.IsPaired)
                {
                    var deviceId = device.Id;
                    button.Click += (_, _) => _ = PairBluetoothQuickAsync(deviceId, device.Name);
                }
                _activityBluetoothDevicesPanel.Children.Add(button);
            }

            if (status.Devices.Count == 0)
            {
                _activityBluetoothDevicesPanel.Children.Add(new TextBlock
                {
                    Text = status.IsEnabled ? "No Bluetooth devices were reported." : "Turn Bluetooth on to view devices.",
                    Margin = new Thickness(0, 8, 0, 0),
                    Foreground = (Brush)FindResource("MutedBrush")
                });
            }

            _ = RefreshActivityHeaderAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _activityBluetoothStatusText.Text = $"Bluetooth unavailable: {ex.Message}";
        }
    }

    private async Task ToggleBluetoothQuickAsync()
    {
        if (_activityBluetoothStatusText is null) return;
        try
        {
            var current = await _activityBluetoothService.GetStatusAsync();
            if (!current.RadioAvailable) return;
            _activityBluetoothStatusText.Text = $"Turning Bluetooth {(current.IsEnabled ? "off" : "on")}…";
            await _activityBluetoothService.SetEnabledAsync(!current.IsEnabled);
            await RefreshBluetoothQuickControlAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _activityBluetoothStatusText.Text = $"Could not change Bluetooth: {ex.Message}";
        }
    }

    private async Task PairBluetoothQuickAsync(string deviceId, string deviceName)
    {
        if (_activityBluetoothStatusText is null) return;
        try
        {
            _activityBluetoothStatusText.Text = $"Pairing {deviceName}…";
            await _activityBluetoothService.PairAsync(deviceId);
            await RefreshBluetoothQuickControlAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _activityBluetoothStatusText.Text = $"Could not pair {deviceName}: {ex.Message}";
        }
    }
}
