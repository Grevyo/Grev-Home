using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrevHome.Input;
using GrevHome.Navigation;

namespace GrevHome;

public partial class MainWindow
{
    private bool _headerNavigationHooked;

    private void Window_HeaderNavigationLoaded(object sender, RoutedEventArgs e)
    {
        InitializeProfilePlayersIntegration();

        if (_headerNavigationHooked)
        {
            return;
        }

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
        Dispatcher.Invoke(() => originalFocus = Keyboard.FocusedElement as Button);
        if (originalFocus is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => CorrectHeaderNavigation(input.Action, originalFocus)));
    }

    private void CorrectHeaderNavigation(InputAction action, Button originalFocus)
    {
        if (_overlayWindow.IsOpen || !originalFocus.IsVisible || !originalFocus.IsEnabled)
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
        var buttons = new[] { ShellBackButton, ProfileBubbleButton, ShellSettingsButton };
        return buttons.Where(IsFocusableButton).ToList();
    }

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
