using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GrevHome.Machine;

namespace GrevHome.Views;

public partial class SettingsView
{
    private readonly AudioService _audioService = new();
    private readonly DisplayService _displayService = new();
    private readonly WifiService _wifiService = new();
    private readonly BluetoothService _bluetoothService = new();
    private IReadOnlyList<AudioOutputDevice> _audioOutputs = Array.Empty<AudioOutputDevice>();
    private int _selectedAudioOutputIndex;
    private IReadOnlyList<DisplayMode> _displayModes = Array.Empty<DisplayMode>();
    private int _selectedDisplayModeIndex;
    private DisplayMode? _pendingDisplayPreviousMode;
    private DispatcherTimer? _displayRevertTimer;
    private int _displayRevertSeconds;
    private bool _bluetoothEnabled;

    private void RefreshAudio()
    {
        try
        {
            var status = _audioService.GetStatus();
            _audioOutputs = _audioService.GetOutputDevices();
            _selectedAudioOutputIndex = Math.Max(0, _audioOutputs
                .Select((device, index) => (device, index))
                .FirstOrDefault(item => string.Equals(item.device.Id, status.OutputDeviceId, StringComparison.OrdinalIgnoreCase))
                .index);

            AudioOutputText.Text = $"Current output: {status.OutputDeviceName}";
            AudioVolumeText.Text = $"Master volume: {status.VolumePercent}%  •  {(status.IsMuted ? "Muted" : "Playing")}";
            AudioMuteButton.Content = status.IsMuted ? "Unmute" : "Mute";
            RenderSelectedAudioOutput();
            AudioStatusText.Text = $"{_audioOutputs.Count} active Windows audio output{(_audioOutputs.Count == 1 ? string.Empty : "s")} detected.";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            AudioStatusText.Text = $"Windows audio refresh failed: {ex.Message}";
        }
    }

    private void RenderSelectedAudioOutput()
    {
        if (_audioOutputs.Count == 0)
        {
            AudioSelectedOutputText.Text = "No active audio output reported by Windows.";
            AudioPreviousOutputButton.IsEnabled = false;
            AudioNextOutputButton.IsEnabled = false;
            AudioApplyOutputButton.IsEnabled = false;
            return;
        }

        _selectedAudioOutputIndex = Math.Clamp(_selectedAudioOutputIndex, 0, _audioOutputs.Count - 1);
        var selected = _audioOutputs[_selectedAudioOutputIndex];
        AudioSelectedOutputText.Text = $"Selected: {selected.Name}{(selected.IsDefault ? "  •  CURRENT" : string.Empty)}";
        AudioPreviousOutputButton.IsEnabled = _audioOutputs.Count > 1;
        AudioNextOutputButton.IsEnabled = _audioOutputs.Count > 1;
        AudioApplyOutputButton.IsEnabled = !selected.IsDefault;
    }

    private void AudioVolumeDown_Click(object sender, RoutedEventArgs e) => AdjustVolume(-10);
    private void AudioVolumeUp_Click(object sender, RoutedEventArgs e) => AdjustVolume(10);

    private void AdjustVolume(int delta)
    {
        try
        {
            var current = _audioService.GetStatus();
            var updated = _audioService.SetVolume(current.VolumePercent + delta);
            AudioVolumeText.Text = $"Master volume: {updated.VolumePercent}%  •  {(updated.IsMuted ? "Muted" : "Playing")}";
            AudioStatusText.Text = $"Volume changed to {updated.VolumePercent}%.";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            AudioStatusText.Text = $"Volume change failed: {ex.Message}";
        }
    }

    private void AudioMute_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = _audioService.GetStatus();
            var updated = _audioService.SetMuted(!current.IsMuted);
            AudioMuteButton.Content = updated.IsMuted ? "Unmute" : "Mute";
            AudioVolumeText.Text = $"Master volume: {updated.VolumePercent}%  •  {(updated.IsMuted ? "Muted" : "Playing")}";
            AudioStatusText.Text = updated.IsMuted ? "Audio muted." : "Audio unmuted.";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            AudioStatusText.Text = $"Mute change failed: {ex.Message}";
        }
    }

    private void AudioPreviousOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_audioOutputs.Count == 0) return;
        _selectedAudioOutputIndex = (_selectedAudioOutputIndex - 1 + _audioOutputs.Count) % _audioOutputs.Count;
        RenderSelectedAudioOutput();
    }

    private void AudioNextOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_audioOutputs.Count == 0) return;
        _selectedAudioOutputIndex = (_selectedAudioOutputIndex + 1) % _audioOutputs.Count;
        RenderSelectedAudioOutput();
    }

    private void AudioApplyOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_audioOutputs.Count == 0) return;
        try
        {
            var selected = _audioOutputs[_selectedAudioOutputIndex];
            var updated = _audioService.SetDefaultOutput(selected.Id);
            AudioStatusText.Text = $"Default audio output changed to {updated.OutputDeviceName}.";
            RefreshAudio();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            AudioStatusText.Text = $"Output change failed: {ex.Message}";
        }
    }

    private void RefreshDisplay()
    {
        try
        {
            var current = _displayService.GetCurrentMode();
            _displayModes = _displayService.GetAvailableModes();
            var exact = _displayModes
                .Select((mode, index) => (mode, index))
                .FirstOrDefault(item => item.mode.Width == current.Width &&
                                        item.mode.Height == current.Height &&
                                        item.mode.RefreshRate == current.RefreshRate);
            _selectedDisplayModeIndex = exact.mode is null
                ? Math.Max(0, _displayModes.Count - 1)
                : exact.index;

            DisplayCurrentModeText.Text = $"Current primary display: {current}";
            DisplayStatusText.Text = $"{_displayModes.Count} validated Windows display mode{(_displayModes.Count == 1 ? string.Empty : "s")} available.";
            RenderSelectedDisplayMode();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            DisplayStatusText.Text = $"Display refresh failed: {ex.Message}";
        }
    }

    private void RenderSelectedDisplayMode()
    {
        if (_displayModes.Count == 0)
        {
            DisplaySelectedModeText.Text = "No display modes reported by Windows.";
            DisplayPreviousModeButton.IsEnabled = false;
            DisplayNextModeButton.IsEnabled = false;
            DisplayApplyModeButton.IsEnabled = false;
            return;
        }

        _selectedDisplayModeIndex = Math.Clamp(_selectedDisplayModeIndex, 0, _displayModes.Count - 1);
        DisplaySelectedModeText.Text = $"Selected: {_displayModes[_selectedDisplayModeIndex]}";
        DisplayPreviousModeButton.IsEnabled = _displayModes.Count > 1 && _pendingDisplayPreviousMode is null;
        DisplayNextModeButton.IsEnabled = _displayModes.Count > 1 && _pendingDisplayPreviousMode is null;
        DisplayApplyModeButton.IsEnabled = _pendingDisplayPreviousMode is null;
    }

    private void DisplayPreviousMode_Click(object sender, RoutedEventArgs e)
    {
        if (_displayModes.Count == 0 || _pendingDisplayPreviousMode is not null) return;
        _selectedDisplayModeIndex = (_selectedDisplayModeIndex - 1 + _displayModes.Count) % _displayModes.Count;
        RenderSelectedDisplayMode();
    }

    private void DisplayNextMode_Click(object sender, RoutedEventArgs e)
    {
        if (_displayModes.Count == 0 || _pendingDisplayPreviousMode is not null) return;
        _selectedDisplayModeIndex = (_selectedDisplayModeIndex + 1) % _displayModes.Count;
        RenderSelectedDisplayMode();
    }

    private void DisplayApplyMode_Click(object sender, RoutedEventArgs e)
    {
        if (_displayModes.Count == 0 || _pendingDisplayPreviousMode is not null) return;

        try
        {
            var previous = _displayService.GetCurrentMode();
            var requested = _displayModes[_selectedDisplayModeIndex];
            if (previous.Width == requested.Width && previous.Height == requested.Height && previous.RefreshRate == requested.RefreshRate)
            {
                DisplayStatusText.Text = "That display mode is already active.";
                return;
            }

            _displayService.ApplyMode(requested);
            _pendingDisplayPreviousMode = previous;
            _displayRevertSeconds = 15;
            DisplayKeepButton.Visibility = Visibility.Visible;
            DisplayRevertButton.Visibility = Visibility.Visible;
            DisplayApplyModeButton.IsEnabled = false;
            DisplayPreviousModeButton.IsEnabled = false;
            DisplayNextModeButton.IsEnabled = false;
            UpdateDisplayCountdownText();

            _displayRevertTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _displayRevertTimer.Tick -= DisplayRevertTimer_Tick;
            _displayRevertTimer.Tick += DisplayRevertTimer_Tick;
            _displayRevertTimer.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            DisplayStatusText.Text = $"Display change failed: {ex.Message}";
        }
    }

    private void DisplayRevertTimer_Tick(object? sender, EventArgs e)
    {
        _displayRevertSeconds--;
        if (_displayRevertSeconds <= 0)
        {
            RevertPendingDisplayMode("Display mode automatically reverted because it was not confirmed.");
            return;
        }

        UpdateDisplayCountdownText();
    }

    private void UpdateDisplayCountdownText() =>
        DisplayStatusText.Text = $"New display mode active. Choose Keep within {_displayRevertSeconds} seconds or Grev Home will restore the previous mode automatically.";

    private void DisplayKeep_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDisplayPreviousMode is null) return;
        StopDisplayRevertTimer();
        _pendingDisplayPreviousMode = null;
        DisplayKeepButton.Visibility = Visibility.Collapsed;
        DisplayRevertButton.Visibility = Visibility.Collapsed;
        DisplayStatusText.Text = "Display mode kept.";
        RefreshDisplay();
    }

    private void DisplayRevert_Click(object sender, RoutedEventArgs e) =>
        RevertPendingDisplayMode("Previous display mode restored.");

    private void RevertPendingDisplayMode(string successMessage)
    {
        var previous = _pendingDisplayPreviousMode;
        if (previous is null) return;

        StopDisplayRevertTimer();
        try
        {
            _displayService.ApplyMode(previous);
            DisplayStatusText.Text = successMessage;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            DisplayStatusText.Text = $"Automatic display revert failed: {ex.Message}";
        }
        finally
        {
            _pendingDisplayPreviousMode = null;
            DisplayKeepButton.Visibility = Visibility.Collapsed;
            DisplayRevertButton.Visibility = Visibility.Collapsed;
            RefreshDisplay();
        }
    }

    private void StopDisplayRevertTimer()
    {
        _displayRevertTimer?.Stop();
    }

    private async Task RefreshConnectionsAsync()
    {
        await RefreshWifiAsync();
        await RefreshBluetoothAsync();
    }

    private async Task RefreshWifiAsync()
    {
        try
        {
            var statusTask = Task.Run(_wifiService.GetStatus);
            var networksTask = Task.Run(_wifiService.GetAvailableNetworks);
            var status = await statusTask;
            var networks = await networksTask;

            WifiStatusText.Text = !status.AdapterAvailable
                ? "No Wi-Fi adapter reported by Windows."
                : status.IsConnected
                    ? $"Connected to {status.Ssid}  •  {status.SignalQuality}% signal  •  {status.AdapterName}"
                    : $"Wi-Fi available  •  not connected  •  {status.AdapterName}";
            WifiDisconnectButton.IsEnabled = status.IsConnected;
            RenderWifiNetworks(networks);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            WifiStatusText.Text = $"Wi-Fi refresh failed: {ex.Message}";
            WifiNetworksPanel.Children.Clear();
        }
    }

    private void RenderWifiNetworks(IReadOnlyList<WifiNetwork> networks)
    {
        WifiNetworksPanel.Children.Clear();
        if (networks.Count == 0)
        {
            WifiNetworksPanel.Children.Add(CreateConnectionInfo("No Wi-Fi networks reported by Windows."));
            return;
        }

        foreach (var network in networks.Take(16))
        {
            var button = new Button
            {
                Tag = network,
                MinHeight = 52,
                Margin = new Thickness(0, 0, 0, 7),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = $"{network.Ssid}  •  {network.SignalQuality}%  •  {(network.IsConnected ? "CONNECTED" : network.CanConnect ? "saved profile" : "profile required")}",
                IsEnabled = network.CanConnect && !network.IsConnected
            };
            button.Click += WifiNetwork_Click;
            WifiNetworksPanel.Children.Add(button);
        }
    }

    private async void WifiNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WifiNetwork network } || string.IsNullOrWhiteSpace(network.ProfileName)) return;
        try
        {
            await Task.Run(() => _wifiService.ConnectSavedProfile(network.ProfileName));
            WifiStatusText.Text = $"Connection requested for {network.Ssid}.";
            await Task.Delay(1200);
            await RefreshWifiAsync();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            WifiStatusText.Text = $"Wi-Fi connection failed: {ex.Message}";
        }
    }

    private async void WifiDisconnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(_wifiService.Disconnect);
            WifiStatusText.Text = "Wi-Fi disconnect requested.";
            await Task.Delay(700);
            await RefreshWifiAsync();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            WifiStatusText.Text = $"Wi-Fi disconnect failed: {ex.Message}";
        }
    }

    private async Task RefreshBluetoothAsync()
    {
        try
        {
            var status = await _bluetoothService.GetStatusAsync();
            _bluetoothEnabled = status.IsEnabled;
            BluetoothStatusText.Text = !status.RadioAvailable
                ? "No Bluetooth radio reported by Windows."
                : status.IsEnabled
                    ? $"Bluetooth is on  •  {status.Devices.Count} known device{(status.Devices.Count == 1 ? string.Empty : "s")}"
                    : "Bluetooth is off.";
            BluetoothToggleButton.IsEnabled = status.RadioAvailable;
            BluetoothToggleButton.Content = status.IsEnabled ? "Turn Bluetooth Off" : "Turn Bluetooth On";
            RenderBluetoothDevices(status.Devices);
        }
        catch (Exception ex)
        {
            BluetoothStatusText.Text = $"Bluetooth refresh failed: {ex.Message}";
            BluetoothDevicesPanel.Children.Clear();
        }
    }

    private void RenderBluetoothDevices(IReadOnlyList<BluetoothDeviceStatus> devices)
    {
        BluetoothDevicesPanel.Children.Clear();
        if (devices.Count == 0)
        {
            BluetoothDevicesPanel.Children.Add(CreateConnectionInfo("No known Bluetooth devices reported by Windows."));
            return;
        }

        foreach (var device in devices.Take(16))
        {
            var button = new Button
            {
                Tag = device,
                MinHeight = 52,
                Margin = new Thickness(0, 0, 0, 7),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = $"{device.Name}  •  {(device.IsPaired ? "PAIRED — select to unpair" : "select to pair")}",
                IsEnabled = _bluetoothEnabled || device.IsPaired
            };
            button.Click += BluetoothDevice_Click;
            BluetoothDevicesPanel.Children.Add(button);
        }
    }

    private async void BluetoothToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _bluetoothService.SetEnabledAsync(!_bluetoothEnabled);
            await RefreshBluetoothAsync();
        }
        catch (Exception ex)
        {
            BluetoothStatusText.Text = $"Bluetooth radio change failed: {ex.Message}";
        }
    }

    private async void BluetoothDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BluetoothDeviceStatus device }) return;
        try
        {
            if (device.IsPaired)
            {
                await _bluetoothService.UnpairAsync(device.Id);
                BluetoothStatusText.Text = $"{device.Name} unpaired.";
            }
            else
            {
                await _bluetoothService.PairAsync(device.Id);
                BluetoothStatusText.Text = $"{device.Name} paired.";
            }

            await RefreshBluetoothAsync();
        }
        catch (Exception ex)
        {
            BluetoothStatusText.Text = $"Bluetooth action failed for {device.Name}: {ex.Message}";
        }
    }

    private async void RefreshConnections_Click(object sender, RoutedEventArgs e) =>
        await RefreshConnectionsAsync();

    private TextBlock CreateConnectionInfo(string text) => new()
    {
        Text = text,
        Margin = new Thickness(2, 4, 0, 8),
        Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        TextWrapping = TextWrapping.Wrap
    };
}
