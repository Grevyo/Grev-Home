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

        if (originalFocus == createAccount && action == InputAction.Up && profiles.Length > 0)
        {
            FocusNearestByHorizontalPosition(profiles, GetCenter(createAccount).X);
            return true;
        }

        if (action != InputAction.Down || !profiles.Contains(originalFocus))
        {
            return false;
        }

        var originalCenter = GetCenter(originalFocus);
        var hasProfileBelow = profiles
            .Where(button => button != originalFocus)
            .Any(button => GetCenter(button).Y > originalCenter.Y + 8);

        if (hasProfileBelow)
        {
            return false;
        }

        createAccount.Focus();
        return true;
    }

    private void CorrectMovementFromHeader(
        Button originalFocus,
        InputAction action,
        List<Button> headerButtons)
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

    private List<Button> GetHeaderButtons()
    {
        var buttons = new[] { ShellBackButton, ProfileBubbleButton, ShellSettingsButton, ShellPowerButton };
        return buttons.Where(IsFocusableButton).ToList();
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
        PowerAppKillerButton.IsEnabled = _session.HasSignedInUsers;
        PowerRunningAppsButton.IsEnabled = _session.HasSignedInUsers;
        PowerMenuStatusText.Text = "Select an action. Power actions require a second press within 8 seconds to confirm.";
        ShellInteractionHost.IsEnabled = false;
        PowerMenuOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PowerAppKillerButton.IsEnabled) PowerAppKillerButton.Focus();
            else PowerSleepButton.Focus();
        }));
    }

    private void ClosePowerMenu(bool returnFocusToHeader = true)
    {
        ResetHeaderPowerConfirmation();
        PowerMenuOverlay.Visibility = Visibility.Collapsed;
        ShellInteractionHost.IsEnabled = true;
        if (returnFocusToHeader)
        {
            Dispatcher.BeginInvoke(new Action(() => ShellPowerButton.Focus()));
        }
    }

    private void ArmOrExecuteHeaderPower(SystemPowerAction action)
    {
        var now = DateTimeOffset.UtcNow;
        if (_headerPendingPowerAction != action || _headerCloseGrevHomeArmed || now > _headerPowerExpiresAt)
        {
            _headerPendingPowerAction = action;
            _headerCloseGrevHomeArmed = false;
            _headerPowerExpiresAt = now.AddSeconds(8);
            UpdatePowerMenuButtons();
            PowerMenuStatusText.Text = $"{FormatHeaderPowerAction(action)} armed. Select it again within 8 seconds to confirm.";
            return;
        }

        ResetHeaderPowerConfirmation();
        try
        {
            PowerMenuStatusText.Text = $"Requesting {FormatHeaderPowerAction(action).ToLowerInvariant()} from Windows…";
            _headerPowerService.Execute(action);
            ClosePowerMenu(returnFocusToHeader: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            PowerMenuStatusText.Text = $"Windows did not complete the power action: {ex.Message}";
        }
    }

    private void ArmOrCloseGrevHome()
    {
        var now = DateTimeOffset.UtcNow;
        if (!_headerCloseGrevHomeArmed || _headerPendingPowerAction is not null || now > _headerPowerExpiresAt)
        {
            _headerPendingPowerAction = null;
            _headerCloseGrevHomeArmed = true;
            _headerPowerExpiresAt = now.AddSeconds(8);
            UpdatePowerMenuButtons();
            PowerMenuStatusText.Text = "Close Grev Home armed. Select it again within 8 seconds to confirm.";
            return;
        }

        ResetHeaderPowerConfirmation();
        Application.Current.Shutdown();
    }

    private void ResetHeaderPowerConfirmation()
    {
        _headerPendingPowerAction = null;
        _headerCloseGrevHomeArmed = false;
        _headerPowerExpiresAt = DateTimeOffset.MinValue;
        UpdatePowerMenuButtons();
    }

    private void UpdatePowerMenuButtons()
    {
        PowerSleepButton.Content = _headerPendingPowerAction == SystemPowerAction.Sleep ? "CONFIRM SLEEP" : "Sleep";
        PowerRestartButton.Content = _headerPendingPowerAction == SystemPowerAction.Restart ? "CONFIRM RESTART" : "Restart";
        PowerShutdownButton.Content = _headerPendingPowerAction == SystemPowerAction.Shutdown ? "CONFIRM SHUT DOWN" : "Shut Down";
        PowerCloseGrevHomeButton.Content = _headerCloseGrevHomeArmed ? "CONFIRM CLOSE GREV HOME" : "Close Grev Home";
    }

    private void PowerAppKiller_Click(object sender, RoutedEventArgs e)
    {
        ClosePowerMenu(returnFocusToHeader: false);
        OpenAppKiller();
    }

    private void PowerRunningApps_Click(object sender, RoutedEventArgs e)
    {
        ClosePowerMenu(returnFocusToHeader: false);
        OpenRunningApps();
    }

    private void PowerSleep_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecuteHeaderPower(SystemPowerAction.Sleep);

    private void PowerRestart_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecuteHeaderPower(SystemPowerAction.Restart);

    private void PowerShutdown_Click(object sender, RoutedEventArgs e) =>
        ArmOrExecuteHeaderPower(SystemPowerAction.Shutdown);

    private void PowerCloseGrevHome_Click(object sender, RoutedEventArgs e) =>
        ArmOrCloseGrevHome();

    private void PowerMenuCancel_Click(object sender, RoutedEventArgs e) =>
        ClosePowerMenu();

    private static string FormatHeaderPowerAction(SystemPowerAction action) => action switch
    {
        SystemPowerAction.Shutdown => "Shut Down",
        SystemPowerAction.Restart => "Restart",
        SystemPowerAction.Sleep => "Sleep",
        _ => action.ToString()
    };

    private void FocusNearestByHorizontalPosition(IEnumerable<Button> buttons, double sourceX)
    {
        buttons
            .OrderBy(button => Math.Abs(GetCenter(button).X - sourceX))
            .FirstOrDefault()
            ?.Focus();
    }

    private Point GetCenter(FrameworkElement element) =>
        element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), this);

    private static bool IsFocusableButton(Button button) =>
        button.IsVisible &&
        button.IsEnabled &&
        button.Focusable &&
        button.ActualWidth > 0 &&
        button.ActualHeight > 0;
}
