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
    private Button? _headerFlyoutReturnButton;

    private bool IsPowerMenuOpen => PowerMenuOverlay.Visibility == Visibility.Visible;

    private void Window_HeaderNavigationLoaded(object sender, RoutedEventArgs e)
    {
        if (_headerNavigationHooked)
        {
            return;
        }

        InitializeShellNavigationFinalization();
        InitializeApplianceLifecycleIntegration();
        RuntimeTestAppRegistrationService.ConfigureForCurrentRun(_paths);
        InitializeRuntimeRecoveryIntegration();
        InitializeAppControllerRuntimeIntegration();
        InitializeProfilePlayersIntegration();
        InitializeDashboardDataIntegration();
        InitializeFilesIntegration();
        InitializeGrevStoreIntegration();
        InitializeActivityCenterIntegration();
        InitializeAppSettingsIntegration();
        InitializeAdminConsoleIntegration();
        InitializeOverlayAppKillerIntegration();

        _headerNavigationHooked = true;
        _controllerInput.ActionPressed += HandleHeaderNavigationInput;
    }

    private void HandleHeaderNavigationInput(ControllerInputEventArgs input)
    {
        if (input.Action is not (InputAction.Up or InputAction.Down or InputAction.Left or InputAction.Right))
        {
            return;
        }

        Button? originalFocus = null;
        Dispatcher.Invoke(() =>
        {
            if (IsStoreModalOpen || IsPowerMenuOpen || GetOpenControllerKeyboard() is not null)
            {
                return;
            }

            originalFocus = Keyboard.FocusedElement as Button;
        });

        if (originalFocus is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => CorrectHeaderNavigation(input.Action, originalFocus)));
    }

    private void CorrectHeaderNavigation(InputAction action, Button originalFocus)
    {
        if (IsStoreModalOpen || IsPowerMenuOpen || GetOpenControllerKeyboard() is not null ||
            _overlayWindow.IsOpen || !originalFocus.IsVisible || !originalFocus.IsEnabled)
        {
            return;
        }

        if (TryCorrectLoginNavigation(action, originalFocus))
        {
            return;
        }

        var currentFocus = Keyboard.FocusedElement as Button;
        if (currentFocus is not null && currentFocus != originalFocus)
        {
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
        var buttons = new Button?[]
        {
            ShellBackButton,
            _activityVolumeButton,
            _activityWifiButton,
            _activityBluetoothButton,
            ProfileBubbleButton,
            ShellSettingsButton,
            ShellPowerButton
        };
        return buttons
            .Where(button => button is not null && IsFocusableButton(button))
            .Select(button => button!)
            .ToList();
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
        HideActivityQuickControls();
        _headerFlyoutReturnButton = ShellPowerButton;
        ProfileQuickMenuCard.Visibility = Visibility.Collapsed;
        PowerMenuCard.Visibility = Visibility.Visible;
        PowerAppKillerButton.IsEnabled = _session.HasSignedInUsers;
        PowerRunningAppsButton.IsEnabled = _session.HasSignedInUsers;
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
        var returnButton = _headerFlyoutReturnButton;
        _headerFlyoutReturnButton = null;
        ResetHeaderPowerConfirmation();
        HideActivityQuickControls();
        ProfileQuickMenuCard.Visibility = Visibility.Collapsed;
        PowerMenuCard.Visibility = Visibility.Collapsed;
        PowerMenuOverlay.Visibility = Visibility.Collapsed;
        ShellInteractionHost.IsEnabled = true;
        if (returnFocusToHeader)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (returnButton is { IsVisible: true, IsEnabled: true }) returnButton.Focus();
                else if (ShellPowerButton.IsVisible && ShellPowerButton.IsEnabled) ShellPowerButton.Focus();
            }));
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
            ShowPowerMenuStatus($"{FormatHeaderPowerAction(action)} armed. Select it again within 8 seconds to confirm.");
            return;
        }

        ResetHeaderPowerConfirmation();
        try
        {
            ShowPowerMenuStatus($"Requesting {FormatHeaderPowerAction(action).ToLowerInvariant()} from Windows…");
            _headerPowerService.Execute(action);
            ClosePowerMenu(returnFocusToHeader: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            ShowPowerMenuStatus($"Windows did not complete the power action: {ex.Message}");
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
            ShowPowerMenuStatus("Close Grev Home armed. Select it again within 8 seconds to confirm.");
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
        ClearPowerMenuStatus();
    }

    private void ShowPowerMenuStatus(string message)
    {
        PowerMenuStatusText.Text = message;
        PowerMenuStatusText.Visibility = Visibility.Visible;
    }

    private void ClearPowerMenuStatus()
    {
        PowerMenuStatusText.Text = string.Empty;
        PowerMenuStatusText.Visibility = Visibility.Collapsed;
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
