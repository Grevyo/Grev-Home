using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Machine;

namespace GrevHome.Views;

/// <summary>
/// First-run Network and Startup steps: a brand-new PC can get online (typing the Wi-Fi password
/// with the controller keyboard) and choose to start Grev Home with Windows before any account
/// exists, so linking grev.dad during account creation works without a keyboard.
/// </summary>
public partial class FirstRunSetupView
{
    private readonly WifiService _wifi = new();
    private readonly WindowsStartupService _startup = new();
    private WifiNetwork? _wifiJoinTarget;
    private bool _wifiBusy;
    private bool _startWithWindows = true;

    private void InitializeNetworkStep()
    {
        WifiKeyboard.Completed += password =>
        {
            if (_wifiJoinTarget is { } network) _ = JoinAsync(network, password);
        };
        WifiKeyboard.Cancelled += (_, _) => _wifiJoinTarget = null;
    }

    private void ResetNetworkStep()
    {
        _wifiJoinTarget = null;
        WifiNetworksPanel.Children.Clear();
        _startWithWindows = _startup.IsAvailable;
        StartupChoiceButton.IsEnabled = _startup.IsAvailable;
        UpdateStartupChoice();
        _ = RefreshWifiStatusAsync();
    }

    private async Task RefreshWifiStatusAsync()
    {
        try
        {
            var status = await Task.Run(_wifi.GetStatus);
            WifiStatusText.Text = !status.AdapterAvailable
                ? "No Wi-Fi adapter found. If this PC uses a network cable, you're ready to go."
                : status.IsConnected
                    ? $"✓ Connected to {status.Ssid}."
                    : "Not connected to Wi-Fi yet.";
            FindWifiButton.IsEnabled = status.AdapterAvailable;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException or DllNotFoundException)
        {
            WifiStatusText.Text = "Wi-Fi isn't available on this PC.";
            FindWifiButton.IsEnabled = false;
        }
    }

    private async void FindWifi_Click(object sender, RoutedEventArgs e)
    {
        if (_wifiBusy) return;
        WifiStatusText.Text = "Looking for networks…";
        try
        {
            var networks = await Task.Run(_wifi.GetAvailableNetworks);
            WifiNetworksPanel.Children.Clear();
            foreach (var network in networks.Take(10))
            {
                var state = network.IsConnected ? "connected"
                    : network.CanConnect ? "saved"
                    : network.CanJoin ? (network.NeedsPassword ? "password" : "open")
                    : "use Windows Settings";
                var button = new Button
                {
                    Tag = network,
                    MinHeight = 48,
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = $"{network.Ssid}  •  {network.SignalQuality}%  •  {state}",
                    IsEnabled = !network.IsConnected
                };
                button.Click += WifiNetwork_Click;
                WifiNetworksPanel.Children.Add(button);
            }
            if (networks.Count == 0) WifiStatusText.Text = "No Wi-Fi networks found. Choose Find Wi-Fi networks to look again.";
            else await RefreshWifiStatusAsync();
            if (WifiNetworksPanel.Children.OfType<Button>().FirstOrDefault(button => button.IsEnabled) is { } first) first.Focus();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            WifiStatusText.Text = $"Couldn't look for networks: {ex.Message}";
        }
    }

    private async void WifiNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (_wifiBusy || sender is not Button { Tag: WifiNetwork network }) return;
        if (network.CanConnect && network.ProfileName is { } profile)
        {
            _wifiBusy = true;
            WifiStatusText.Text = $"Connecting to {network.Ssid}…";
            try
            {
                await Task.Run(() => _wifi.ConnectSavedProfile(profile));
                await Task.Delay(2500);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
            {
                WifiStatusText.Text = $"Couldn't connect: {ex.Message}";
            }
            finally
            {
                _wifiBusy = false;
            }
            await RefreshWifiStatusAsync();
            return;
        }
        if (!network.CanJoin)
        {
            WifiStatusText.Text = $"{network.Ssid} uses {WifiProfileBuilder.Describe(network.Security)} security, which needs Windows Settings. Pick another network or skip this for now.";
            return;
        }
        if (network.NeedsPassword)
        {
            _wifiJoinTarget = network;
            WifiKeyboard.Open($"Password for {network.Ssid}", string.Empty, 64, password: true);
            return;
        }
        await JoinAsync(network, null);
    }

    private async Task JoinAsync(WifiNetwork network, string? password)
    {
        _wifiJoinTarget = null;
        _wifiBusy = true;
        WifiStatusText.Text = $"Connecting to {network.Ssid}…";
        try
        {
            var result = await _wifi.JoinAsync(network, password);
            WifiStatusText.Text = result.Connected ? $"✓ {result.Message}" : result.Message;
            if (result.Connected) WifiNetworksPanel.Children.Clear();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException or ArgumentException or NotSupportedException)
        {
            WifiStatusText.Text = $"Couldn't connect to {network.Ssid}: {ex.Message}";
        }
        finally
        {
            _wifiBusy = false;
        }
    }

    private void ToggleStartupChoice_Click(object sender, RoutedEventArgs e)
    {
        _startWithWindows = !_startWithWindows;
        UpdateStartupChoice();
    }

    private void UpdateStartupChoice() =>
        StartupChoiceButton.Content = !_startup.IsAvailable
            ? "Start with Windows is available once Grev Home is installed"
            : _startWithWindows
                ? "✓  Start Grev Home when Windows starts"
                : "○  Don't start Grev Home with Windows";
}
