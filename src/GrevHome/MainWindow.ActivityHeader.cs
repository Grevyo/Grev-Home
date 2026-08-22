using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private Border? _activityVolumeFlyout;
    private TextBlock? _activityVolumeValueText;
    private TextBlock? _activityVolumeOutputText;
    private ProgressBar? _activityVolumeProgress;
    private Button? _activityVolumeDownButton;
    private Button? _activityVolumeMuteButton;
    private Button? _activityVolumeUpButton;
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

        _activityVolumeButton = CreateActivityHeaderButton("🔊 --%", (_, _) => OpenVolumeQuickControl());
        _activityWifiButton = CreateActivityHeaderButton("Wi-Fi --", (_, _) => OpenActivityConnectionsSettings());
        _activityBluetoothButton = CreateActivityHeaderButton("BT --", (_, _) => OpenActivityConnectionsSettings());
        host.Children.Add(_activityVolumeButton);
        host.Children.Add(_activityWifiButton);
        host.Children.Add(_activityBluetoothButton);
        headerGrid.Children.Add(host);
        BuildVolumeQuickControl();

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

    private void BuildVolumeQuickControl()
    {
        if (_activityVolumeFlyout is not null || PowerMenuOverlay.Child is not Grid overlayGrid)
        {
            return;
        }

        _activityVolumeFlyout = new Border
        {
            Width = 360,
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 70, 95)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_activityVolumeFlyout, 120);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "VOLUME",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });

        _activityVolumeValueText = new TextBlock
        {
            Text = "--%",
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold
        };
        content.Children.Add(_activityVolumeValueText);

        _activityVolumeProgress = new ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 10, 0, 0)
        };
        content.Children.Add(_activityVolumeProgress);

        _activityVolumeOutputText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 14),
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(_activityVolumeOutputText);

        var controls = new UniformGrid { Columns = 3 };
        _activityVolumeDownButton = CreateVolumeQuickButton("− 10", (_, _) => ChangeQuickVolume(-10));
        _activityVolumeMuteButton = CreateVolumeQuickButton("Mute", (_, _) => ToggleQuickMute());
        _activityVolumeUpButton = CreateVolumeQuickButton("+ 10", (_, _) => ChangeQuickVolume(10));
        controls.Children.Add(_activityVolumeDownButton);
        controls.Children.Add(_activityVolumeMuteButton);
        controls.Children.Add(_activityVolumeUpButton);
        content.Children.Add(controls);

        var close = new Button
        {
            Content = "Close",
            MinHeight = 46,
            Margin = new Thickness(0, 10, 0, 0)
        };
        close.Click += (_, _) => ClosePowerMenu();
        content.Children.Add(close);

        _activityVolumeFlyout.Child = content;
        overlayGrid.Children.Add(_activityVolumeFlyout);
    }

    private static Button CreateVolumeQuickButton(string content, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = content,
            MinHeight = 50,
            Margin = new Thickness(4)
        };
        button.Click += click;
        return button;
    }

    private void OpenVolumeQuickControl()
    {
        if (_activityVolumeButton is null || _activityVolumeFlyout is null || IsStoreModalOpen)
        {
            return;
        }

        if (IsPowerMenuOpen)
        {
            ClosePowerMenu(returnFocusToHeader: false);
        }

        ResetHeaderPowerConfirmation();
        _headerFlyoutReturnButton = _activityVolumeButton;
        ProfileQuickMenuCard.Visibility = Visibility.Collapsed;
        PowerMenuCard.Visibility = Visibility.Collapsed;
        HideActivityQuickControls();

        var anchor = _activityVolumeButton.TranslatePoint(
            new Point(0, _activityVolumeButton.ActualHeight),
            PowerMenuOverlay);
        var left = Math.Clamp(anchor.X, 16, Math.Max(16, ActualWidth - _activityVolumeFlyout.Width - 16));
        _activityVolumeFlyout.Margin = new Thickness(left, anchor.Y + 8, 0, 0);
        _activityVolumeFlyout.Visibility = Visibility.Visible;
        ShellInteractionHost.IsEnabled = false;
        PowerMenuOverlay.Visibility = Visibility.Visible;
        RefreshVolumeQuickControl();
        Dispatcher.BeginInvoke(new Action(() => _activityVolumeDownButton?.Focus()));
    }

    private void HideActivityQuickControls()
    {
        if (_activityVolumeFlyout is not null)
        {
            _activityVolumeFlyout.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshVolumeQuickControl(AudioStatus? knownStatus = null)
    {
        if (_activityVolumeValueText is null || _activityVolumeProgress is null || _activityVolumeOutputText is null)
        {
            return;
        }

        try
        {
            var status = knownStatus ?? _activityAudioService.GetStatus();
            _activityVolumeValueText.Text = status.IsMuted ? $"{status.VolumePercent}% • MUTED" : $"{status.VolumePercent}%";
            _activityVolumeProgress.Value = status.VolumePercent;
            _activityVolumeOutputText.Text = status.OutputDeviceName;
            if (_activityVolumeMuteButton is not null)
            {
                _activityVolumeMuteButton.Content = status.IsMuted ? "Unmute" : "Mute";
            }
            ApplyAudioHeaderStatus(status);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _activityVolumeValueText.Text = "Unavailable";
            _activityVolumeProgress.Value = 0;
            _activityVolumeOutputText.Text = ex.Message;
        }
    }

    private void ChangeQuickVolume(int delta)
    {
        try
        {
            var current = _activityAudioService.GetStatus();
            var updated = _activityAudioService.SetVolume(current.VolumePercent + delta);
            RefreshVolumeQuickControl(updated);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (_activityVolumeOutputText is not null) _activityVolumeOutputText.Text = ex.Message;
        }
    }

    private void ToggleQuickMute()
    {
        try
        {
            var current = _activityAudioService.GetStatus();
            var updated = _activityAudioService.SetMuted(!current.IsMuted);
            RefreshVolumeQuickControl(updated);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (_activityVolumeOutputText is not null) _activityVolumeOutputText.Text = ex.Message;
        }
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

            if (audio is not null)
            {
                ApplyAudioHeaderStatus(audio);
                if (_activityVolumeFlyout?.Visibility == Visibility.Visible)
                {
                    RefreshVolumeQuickControl(audio);
                }
            }
            else if (_activityVolumeButton is not null)
            {
                _activityVolumeButton.Content = "🔊 --%";
                _activityVolumeButton.ToolTip = "Audio status unavailable";
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

    private void ApplyAudioHeaderStatus(AudioStatus audio)
    {
        if (_activityVolumeButton is null) return;
        _activityVolumeButton.Content = audio.IsMuted ? "🔇 Muted" : $"🔊 {audio.VolumePercent}%";
        _activityVolumeButton.ToolTip = audio.OutputDeviceName;
    }

    private void OpenActivityConnectionsSettings()
    {
        OpenSettings();
        _settingsView.OpenConnectionsSection();
    }
}
