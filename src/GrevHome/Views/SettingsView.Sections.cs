using System.Windows;
using System.Windows.Controls;

namespace GrevHome.Views;

public partial class SettingsView
{
    private void AccountSectionButton_Click(object sender, RoutedEventArgs e) =>
        ToggleSettingsSection(AccountSectionButton, AccountSectionContent, "ACCOUNT");

    private void ControllerShortcutsSectionButton_Click(object sender, RoutedEventArgs e) =>
        ToggleSettingsSection(
            ControllerShortcutsSectionButton,
            ControllerShortcutsSectionContent,
            "CONTROLLER SYSTEM SHORTCUTS");

    private void AudioSectionButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsSection(AudioSectionButton, AudioSectionContent, "AUDIO");
        if (AudioSectionContent.Visibility == Visibility.Visible)
        {
            RefreshAudio();
        }
    }

    private void DisplaySectionButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsSection(DisplaySectionButton, DisplaySectionContent, "DISPLAY");
        if (DisplaySectionContent.Visibility == Visibility.Visible)
        {
            RefreshDisplay();
        }
    }

    private async void ConnectionsSectionButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsSection(ConnectionsSectionButton, ConnectionsSectionContent, "CONNECTIONS");
        if (ConnectionsSectionContent.Visibility == Visibility.Visible)
        {
            await RefreshConnectionsAsync();
        }
    }

    private void SystemStatusSectionButton_Click(object sender, RoutedEventArgs e) =>
        ToggleSettingsSection(SystemStatusSectionButton, SystemStatusSectionContent, "SYSTEM STATUS");

    private void PowerSectionButton_Click(object sender, RoutedEventArgs e) =>
        ToggleSettingsSection(PowerSectionButton, PowerSectionContent, "POWER");

    public void OpenAudioSection()
    {
        EnsureSettingsSectionOpen(AudioSectionButton, AudioSectionContent, "AUDIO");
        RefreshAudio();
    }

    public async void OpenConnectionsSection()
    {
        EnsureSettingsSectionOpen(ConnectionsSectionButton, ConnectionsSectionContent, "CONNECTIONS");
        await RefreshConnectionsAsync();
    }

    private static void ToggleSettingsSection(Button header, UIElement content, string title)
    {
        var expand = content.Visibility != Visibility.Visible;
        content.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        header.Content = $"{title}  {(expand ? "▴" : "▾")}";

        header.Focus();
    }

    private static void EnsureSettingsSectionOpen(Button header, UIElement content, string title)
    {
        content.Visibility = Visibility.Visible;
        header.Content = $"{title}  ▴";
        header.Focus();
    }
}
