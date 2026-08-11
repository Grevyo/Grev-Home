using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrevHome.Input;
using GrevHome.Machine;
using GrevHome.Navigation;
using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private readonly SystemPowerService _headerPowerService = new();
    private SystemPowerAction? _headerPendingPowerAction;
    private DateTimeOffset _headerPowerExpiresAt;
    private bool _headerCloseGrevHomeArmed;
    private bool _headerNavigationHooked;

    private bool IsPowerMenuOpen => PowerMenuOverlay.Visibility == Visibility.Visible;

    private void Window_HeaderNavigationLoaded(object sender, RoutedEventArgs e)
    {
        if (_headerNavigationHooked)
        {
            return;
        }

        RuntimeTestAppRegistrationService.ConfigureForCurrentRun(_paths);
        InitializeProfilePlayersIntegration();
        InitializeGrevStoreIntegration();
        InitializeAppSettingsIntegration();
        InitializeAdminConsoleIntegration();

        _headerNavigationHooked = true;
        _controllerInput.ActionPressed += HandleHeaderNavigationInput;
    }

    private void HandleHeaderNavigationInput(ControllerInputEventArgs input)
    {
        if (IsStoreModalOpen || IsPowerMenuOpen ||
            input.Action is not (InputAction.Up or InputAction.Down or InputAction.Left or InputAction.Right))
        {
            return;
        }

        Button? originalFocus = null;
        Dispatcher.Invoke(() => originalFocus = Keyboard.FocusedElement as Button);
        if (originalFocus is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => CorrectHeaderNavigation(input.Action, originalFocus)));
    }

    private void CorrectHeaderNavigation(InputAction action, Button originalFocus)
    {
        if (IsStoreModalOpen || IsPowerMenuOpen || _overlayWindow.IsOpen || !originalFocus.IsVisible || !originalFocus.IsEnabled)
        {
            return;
        }

        // The Login screen intentionally has profile cards in a scrolling WrapPanel and
        // Create Account in a separate row. Give that boundary an explicit controller rule
        // instead of relying on WPF to infer a spatial relationship across containers.
        if (TryCorrectLoginNavigation(action, originalFocus))
        {
            return;
        }

        var currentFocus = Keyboard.FocusedElement as Button;
        if (currentFocus is not null && currentFocus != originalFocus)
        {
            // Normal WPF directional navigation succeeded; do not add a second movement.
            return;
        }

        var headerButtons = GetHeaderButtons();
        if (headerButtons.Count == 0)
        {
            return;
        }

        if (headerButtons.Contains(originalFocus))
        {
            CorrectMovementFromHeader(originalFocus, action, headerButtons);
            return;
        }

        if (!RouteHost.IsAncestorOf(originalFocus) || action != InputAction.Up)
        {
            return;
        }

        var originalCenter = GetCenter(originalFocus);
        var routeButtons = FindVisualChildren<Button>(RouteHost)
            .Where(IsFocusableButton)
            .Where(button => button != originalFocus)
            .ToArray();

        // Only leave the page when the focused control is already on the top-most reachable row.
        if (routeButtons.Any(button => GetCenter(button).Y < originalCenter.Y - 8))
        {
            return;
        }

        FocusNearestByHorizontalPosition(headerButtons, originalCenter.X);
    }

    private bool TryCorrectLoginNavigation(InputAction action, Button originalFocus)
    {
        if (_navigation.Current != Route.Login)
        {
            return false;
        }

        var createAccount = _loginView.CreateAccountFocusTarget;
        var profiles = _loginView.ProfileFocusTargets
            .Where(IsFocusableButton)
            .ToArray();

        if (profiles.Length == 0 || !IsFocusableButton(createAccount))
        {
            return false;
        }

        if (originalFocus == createAccount && action == InputAction.Up)
        {
            FocusNearestByHorizontalPosition(profiles, GetCenter(createAccount).X);
            return true;
        }

        if (profiles.Contains(originalFocus) && action == InputAction.Down)
        {
            var originalCenter = GetCenter(originalFocus);
            var lowerProfile = profiles
                .Where(button => button != originalFocus)
                .Select(button => (Button: button, Center: GetCenter(button)))
                .Where(item => item.Center.Y > originalCenter.Y + 8)
                .OrderBy(item => item.Center.Y - originalCenter.Y)
                .ThenBy(item => Math.Abs(item.Center.X - originalCenter.X))
                .Select(item => item.Button)
                .FirstOrDefault();
            if (lowerProfile is not null)
            {
                lowerProfile.Focus();
                return true;
            }

            createAccount.Focus();
            return true;
        }

        return false;
    }

    private void CorrectMovementFromHeader(Button originalFocus, InputAction action, IReadOnlyList<Button> headerButtons)
    {
        var index = headerButtons.IndexOf(originalFocus);
        if (index < 0)
        {
            return;
        }

        switch (action)
        {
            case InputAction.Left when index > 0:
                headerButtons[index - 1].Focus();
                break;
            case InputAction.Right when index < headerButtons.Count - 1:
                headerButtons[index + 1].Focus();
                break;
            case InputAction.Down:
            {
                var routeButtons = FindVisualChildren<Button>(RouteHost)
                    .Where(IsFocusableButton)
                    .ToArray();
                if (routeButtons.Length > 0)
                {
                    FocusNearestByHorizontalPosition(routeButtons, GetCenter(originalFocus).X);
                }
                break;
            }
        }
    }

    private IReadOnlyList<Button> GetHeaderButtons()
    {
        var buttons = new[]
        {
            ShellBackButton,
            ShellPlayerButton,
            ShellSettingsButton,
            ShellPowerButton
        };

        return buttons.Where(IsFocusableButton).ToArray();
    }

    private static bool IsFocusableButton(Button? button) =>
        button is { IsVisible: true, IsEnabled: true, Focusable: true };

    private static Point GetCenter(FrameworkElement element)
    {
        try
        {
            var point = element.TranslatePoint(new Point(element.ActualWidth / 2d, element.ActualHeight / 2d), Application.Current.MainWindow);
            return point;
        }
        catch (InvalidOperationException)
        {
            return new Point();
        }
    }

    private static void FocusNearestByHorizontalPosition(IEnumerable<Button> buttons, double x)
    {
        buttons
            .Select(button => (Button: button, Center: GetCenter(button)))
            .OrderBy(item => Math.Abs(item.Center.X - x))
            .ThenBy(item => item.Center.Y)
            .Select(item => item.Button)
            .FirstOrDefault()
            ?.Focus();
    }

    private void ShellBack_Click(object sender, RoutedEventArgs e)
    {
        HandleShellBack();
    }

    private void ShellPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (IsStoreModalOpen || IsPowerMenuOpen)
        {
            return;
        }

        OpenProfilePlayersMenu();
    }

    private void ShellSettings_Click(object sender, RoutedEventArgs e)
    {
        if (IsStoreModalOpen || IsPowerMenuOpen)
        {
            return;
        }

        OpenSettings();
    }

    private void ShellPower_Click(object sender, RoutedEventArgs e)
    {
        if (IsStoreModalOpen)
        {
            return;
        }

        if (IsPowerMenuOpen)
        {
            ClosePowerMenu();
            return;
        }

        OpenPowerMenu();
    }

    private void OpenPowerMenu()
    {
        ResetHeaderPowerConfirmation();
        ShellInteractionHost.IsEnabled = false;
        PowerMenuOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => PowerAppKillerButton.Focus()));
    }

    private void ClosePowerMenu()
    {
        if (!IsPowerMenuOpen)
        {
            return;
        }

        ResetHeaderPowerConfirmation();
        PowerMenuOverlay.Visibility = Visibility.Collapsed;
        ShellInteractionHost.IsEnabled = true;
        FocusRouteSoon();
    }

    private void PowerAppKiller_Click(object sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        OpenAppKiller();
    }

    private void PowerRunningApps_Click(object sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
        OpenRunningApps();
    }

    private void PowerSleep_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecuteHeaderPower(SystemPowerAction.Sleep);

    private void PowerRestart_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecuteHeaderPower(SystemPowerAction.Restart);

    private void PowerShutdown_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecuteHeaderPower(SystemPowerAction.Shutdown);

    private void PowerCloseGrevHome_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_headerCloseGrevHomeArmed || now > _headerPowerExpiresAt)
        {
            _headerPendingPowerAction = null;
            _headerCloseGrevHomeArmed = true;
            _headerPowerExpiresAt = now.AddSeconds(8);
            PowerStatusText.Text = "Close Grev Home armed. Select Close Grev Home again within 8 seconds to confirm.";
            PowerCloseGrevHomeButton.Content = "CONFIRM CLOSE GREV HOME";
            UpdateHeaderPowerButtons();
            return;
        }

        _headerCloseGrevHomeArmed = false;
        Close();
    }

    private void PowerCancel_Click(object sender, RoutedEventArgs e)
    {
        ClosePowerMenu();
    }

    private void ArmOrExecuteHeaderPower(SystemPowerAction action)
    {
        var now = DateTimeOffset.UtcNow;
        if (_headerPendingPowerAction != action || now > _headerPowerExpiresAt)
        {
            _headerCloseGrevHomeArmed = false;
            _headerPendingPowerAction = action;
            _headerPowerExpiresAt = now.AddSeconds(8);
            PowerStatusText.Text = $"{FormatHeaderPowerAction(action)} armed. Select the same action again within 8 seconds to confirm.";
            PowerCloseGrevHomeButton.Content = "Close Grev Home";
            UpdateHeaderPowerButtons();
            return;
        }

        ResetHeaderPowerConfirmation();
        try
        {
            PowerStatusText.Text = $"Requesting {FormatHeaderPowerAction(action).ToLowerInvariant()} from Windows…";
            _headerPowerService.Execute(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            PowerStatusText.Text = $"Windows did not complete the power action: {ex.Message}";
        }
    }

    private void ResetHeaderPowerConfirmation()
    {
        _headerPendingPowerAction = null;
        _headerPowerExpiresAt = DateTimeOffset.MinValue;
        _headerCloseGrevHomeArmed = false;
        PowerStatusText.Text = "Choose a system action. Power actions require a second confirmation.";
        PowerCloseGrevHomeButton.Content = "Close Grev Home";
        UpdateHeaderPowerButtons();
    }

    private void UpdateHeaderPowerButtons()
    {
        PowerSleepButton.Content = _headerPendingPowerAction == SystemPowerAction.Sleep ? "CONFIRM SLEEP" : "Sleep";
        PowerRestartButton.Content = _headerPendingPowerAction == SystemPowerAction.Restart ? "CONFIRM RESTART" : "Restart";
        PowerShutdownButton.Content = _headerPendingPowerAction == SystemPowerAction.Shutdown ? "CONFIRM SHUT DOWN" : "Shut Down";
    }

    private static string FormatHeaderPowerAction(SystemPowerAction action) => action switch
    {
        SystemPowerAction.Shutdown => "Shut Down",
        SystemPowerAction.Restart => "Restart",
        SystemPowerAction.Sleep => "Sleep",
        _ => action.ToString()
    };
}
