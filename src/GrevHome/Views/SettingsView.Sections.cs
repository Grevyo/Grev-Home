using System.Windows;
using System.Windows.Controls;

namespace GrevHome.Views;

public enum SettingsPage
{
    Account,
    ControllerShortcuts,
    Audio,
    Display,
    Connections,
    SystemInformation,
    Power
}

public partial class SettingsView
{
    private void AccountSectionButton_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsSection(AccountSection, AccountSectionContent, "Account");

    private void ControllerShortcutsSectionButton_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsSection(
            ControllerShortcutsSection,
            ControllerShortcutsSectionContent,
            "Controller System Shortcuts");

    private void AudioSectionButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsSection(AudioSection, AudioSectionContent, "Audio");
        RefreshAudio();
    }

    private void DisplaySectionButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsSection(DisplaySection, DisplaySectionContent, "Display");
        RefreshDisplay();
    }

    private async void ConnectionsSectionButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsSection(ConnectionsSection, ConnectionsSectionContent, "Connections");
        await RefreshConnectionsAsync();
    }

    private void SystemStatusSectionButton_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsSection(SystemStatusSection, SystemStatusSectionContent, "System Information");

    private void PowerSectionButton_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsSection(PowerSection, PowerSectionContent, "Power");

    public void OpenAudioSection()
    {
        OpenSettingsSection(AudioSection, AudioSectionContent, "Audio");
        RefreshAudio();
    }

    public async void OpenConnectionsSection()
    {
        OpenSettingsSection(ConnectionsSection, ConnectionsSectionContent, "Connections");
        await RefreshConnectionsAsync();
    }

    public void OpenSettingsPage(SettingsPage page)
    {
        switch (page)
        {
            case SettingsPage.Account:
                OpenSettingsSection(AccountSection, AccountSectionContent, "Account");
                break;
            case SettingsPage.ControllerShortcuts:
                OpenSettingsSection(ControllerShortcutsSection, ControllerShortcutsSectionContent, "Controller System Shortcuts");
                break;
            case SettingsPage.Audio:
                OpenAudioSection();
                break;
            case SettingsPage.Display:
                OpenSettingsSection(DisplaySection, DisplaySectionContent, "Display");
                RefreshDisplay();
                break;
            case SettingsPage.Connections:
                OpenConnectionsSection();
                break;
            case SettingsPage.SystemInformation:
                OpenSettingsSection(SystemStatusSection, SystemStatusSectionContent, "System Information");
                break;
            case SettingsPage.Power:
                OpenSettingsSection(PowerSection, PowerSectionContent, "Power");
                break;
        }
    }

    public void ShowSettingsHub()
    {
        SettingsHub.Visibility=Visibility.Visible;
        SettingsDetailHeader.Visibility=Visibility.Collapsed;
        foreach(var section in SettingsSections()) section.Visibility=Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(()=>AccountSectionButton.Focus()));
    }

    public bool TryReturnToSettingsHub()
    {
        if(SettingsHub.Visibility==Visibility.Visible)return false;
        ShowSettingsHub();
        return true;
    }

    private void OpenSettingsSection(StackPanel selected, UIElement content, string title)
    {
        SettingsHub.Visibility=Visibility.Collapsed;
        SettingsDetailHeader.Visibility=Visibility.Visible;
        SettingsDetailTitle.Text=title;
        foreach(var section in SettingsSections()) section.Visibility=section==selected?Visibility.Visible:Visibility.Collapsed;
        content.Visibility=Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(()=>FindButtons(content).FirstOrDefault()?.Focus()));
    }

    private IEnumerable<StackPanel> SettingsSections() =>
        [AccountSection,ControllerShortcutsSection,AudioSection,DisplaySection,ConnectionsSection,SystemStatusSection,PowerSection];

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,index);
            if(child is Button button && button.IsVisible && button.IsEnabled)yield return button;
            foreach(var nested in FindButtons(child))yield return nested;
        }
    }

    private void BackToSettings_Click(object sender,RoutedEventArgs e)=>ShowSettingsHub();
}
