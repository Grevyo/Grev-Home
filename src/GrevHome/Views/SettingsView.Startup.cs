using System.Security;
using System.Windows;
using GrevHome.Machine;

namespace GrevHome.Views;

public partial class SettingsView
{
    private readonly WindowsStartupService _startup = new();

    private void RefreshStartWithWindows()
    {
        if (!_startup.IsAvailable)
        {
            StartWithWindowsButton.IsEnabled = false;
            StartWithWindowsButton.Content = "Start with Windows: unavailable";
            StartWithWindowsStatusText.Text = "Only an installed copy of Grev Home can start with Windows.";
            return;
        }
        try
        {
            var enabled = _startup.IsEnabled();
            StartWithWindowsButton.IsEnabled = true;
            StartWithWindowsButton.Content = enabled ? "Start with Windows: On" : "Start with Windows: Off";
            StartWithWindowsStatusText.Text = enabled
                ? "Grev Home opens when you sign in to Windows."
                : "Grev Home opens only when you start it.";
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            StartWithWindowsStatusText.Text = $"Windows startup settings could not be read: {ex.Message}";
        }
    }

    private void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _startup.SetEnabled(!_startup.IsEnabled());
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            StartWithWindowsStatusText.Text = $"Couldn't change Windows startup: {ex.Message}";
            return;
        }
        RefreshStartWithWindows();
    }
}
