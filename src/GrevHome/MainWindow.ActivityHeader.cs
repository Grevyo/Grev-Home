using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GrevHome.Machine;

namespace GrevHome;

public partial class MainWindow
{
    private readonly AudioService _activityAudioService = new();
    private readonly WifiService _activityWifiService = new();
    private readonly BluetoothService _activityBluetoothService = new();
    private readonly DispatcherTimer _activityHeaderTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private Button? _activityVolumeButton;
    private Button? _activityWifiButton;
    private Button? _activityBluetoothButton;
    private bool _activityHeaderReady;
    private bool _activityHeaderRefreshing;

    private void InitializeActivityHeader()
    {
        if (_activityHeaderReady || ProfileBubbleButton.Parent is not Grid headerGrid)
        {
            return;
        }

        _activityHeaderReady = true;
        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0)
        };
        Grid.SetColumn(host, 2);

        _activityVolumeButton = CreateActivityHeaderButton("🔊 --%", (_, _) => OpenActivityAudioSettings());
        _activityWifiButton = CreateActivityHeaderButton("Wi-Fi --", (_, _) => OpenActivityConnectionsSettings());
        _activityBluetoothButton = CreateActivityHeaderButton("BT --", (_, _) => OpenActivityConnectionsSettings());
        host.Children.Add(_activityVolumeButton);
        host.Children.Add(_activityWifiButton);
        host.Children.Add(_activityBluetoothButton);
        headerGrid.Children.Add(host);

        _activityHeaderTimer.Tick += (_, _) => _ = RefreshActivityHeaderAsync();
        _activityHeaderTimer.Start();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _ = RefreshActivityHeaderAsync();
            }
        };
        Closed += (_, _) => _activityHeaderTimer.Stop();
        _ = RefreshActivityHeaderAsync();
    }

    private Button CreateActivityHeaderButton(string content, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 82,
            Height = 44,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            Focusable = true,
            IsTabStop = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += click;
        return button;
    }

    private async Task RefreshActivityHeaderAsync()
    {
        if (_activityHeaderRefreshing)
        {
            return;
        }

        _activityHeaderRefreshing = true;
        try
        {
            AudioStatus? audio = null;
            WifiStatus? wifi = null;
            BluetoothStatus? bluetooth = null;

            try { audio = await Task.Run(_activityAudioService.GetStatus); } catch { }
            try { wifi = await Task.Run(_activityWifiService.GetStatus); } catch { }
            try { bluetooth = await _activityBluetoothService.GetStatusAsync(); } catch { }

            if (_activityVolumeButton is not null)
            {
                _activityVolumeButton.Content = audio is null
                    ? "🔊 --%"
                    : audio.IsMuted ? "🔇 Muted" : $"🔊 {audio.VolumePercent}%";
                _activityVolumeButton.ToolTip = audio?.OutputDeviceName ?? "Audio status unavailable";
            }

            if (_activityWifiButton is not null)
            {
                _activityWifiButton.Content = wifi switch
                {
                    null => "Wi-Fi --",
                    { AdapterAvailable: false } => "Wi-Fi N/A",
                    { IsConnected: false } => "Wi-Fi Off",
                    _ => $"Wi-Fi {wifi.SignalQuality}%"
                };
                _activityWifiButton.ToolTip = wifi?.IsConnected == true
                    ? $"{wifi.Ssid} • {wifi.SignalQuality}%"
                    : wifi?.AdapterAvailable == true ? "Wi-Fi disconnected" : "No Wi-Fi adapter";
            }

            if (_activityBluetoothButton is not null)
            {
                _activityBluetoothButton.Content = bluetooth switch
                {
                    null => "BT --",
                    { RadioAvailable: false } => "BT N/A",
                    { IsEnabled: true } => "BT On",
                    _ => "BT Off"
                };
                _activityBluetoothButton.ToolTip = bluetooth?.RadioAvailable == true
                    ? $"Bluetooth {(bluetooth.IsEnabled ? "on" : "off")} • {bluetooth.Devices.Count(device => device.IsPaired)} paired"
                    : "Bluetooth unavailable";
            }
        }
        finally
        {
            _activityHeaderRefreshing = false;
        }
    }

    private void OpenActivityAudioSettings()
    {
        OpenSettings();
        _settingsView.OpenAudioSection();
    }

    private void OpenActivityConnectionsSettings()
    {
        OpenSettings();
        _settingsView.OpenConnectionsSection();
    }
}
