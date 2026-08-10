using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Sessions;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow : Window
{
    private readonly NavigationService _navigation = new();
    private readonly SessionContext _session = new();
    private readonly ControllerInputService _controllerInput = new();
    private readonly bool[] _controllers = new bool[4];
    private readonly LoginView _loginView = new();
    private readonly DashboardView _dashboardView = new();

    public MainWindow()
    {
        InitializeComponent();

        _navigation.RouteChanged += route => Dispatcher.Invoke(() => ShowRoute(route));
        _loginView.SignInRequested += kind => SignIn(kind);
        _dashboardView.LogoutRequested += (_, _) => Logout();

        _controllerInput.ActionPressed += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleInput(input.Action)));
        _controllerInput.ConnectionChanged += change =>
            Dispatcher.BeginInvoke(new Action(() => UpdateControllerStatus(change)));
        _controllerInput.ReturnHomeRequested += _ =>
            Dispatcher.BeginInvoke(new Action(BringGrevHomeToFront));
        _controllerInput.Start();

        Loaded += (_, _) =>
        {
            _navigation.Reset(Route.Login);
            FocusFirstButton();
        };

        Closed += (_, _) => _controllerInput.Dispose();
    }

    private void SignIn(AccountKind kind)
    {
        var displayName = kind == AccountKind.Guest ? "Guest" : "Local User";
        _session.SignInSinglePrimary(displayName, kind);
        _dashboardView.SetPrimaryUser(_session.PrimaryUser);
        _navigation.Navigate(Route.Dashboard);
    }

    private void Logout()
    {
        _session.SignOutAll();
        _navigation.Reset(Route.Login);
    }

    private void ShowRoute(Route route)
    {
        RouteHost.Content = route switch
        {
            Route.Login => _loginView,
            Route.Dashboard => _dashboardView,
            _ => _loginView
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var action = e.Key switch
        {
            Key.Up => InputAction.Up,
            Key.Down => InputAction.Down,
            Key.Left => InputAction.Left,
            Key.Right => InputAction.Right,
            Key.Enter or Key.Space => InputAction.Accept,
            Key.Escape or Key.Back => InputAction.Back,
            _ => (InputAction?)null
        };

        if (action is null)
        {
            return;
        }

        HandleInput(action.Value);
        e.Handled = true;
    }

    private void HandleInput(InputAction action)
    {
        switch (action)
        {
            case InputAction.Up:
                MoveFocus(FocusNavigationDirection.Up);
                break;
            case InputAction.Down:
                MoveFocus(FocusNavigationDirection.Down);
                break;
            case InputAction.Left:
                MoveFocus(FocusNavigationDirection.Left);
                break;
            case InputAction.Right:
                MoveFocus(FocusNavigationDirection.Right);
                break;
            case InputAction.Accept:
                ActivateFocusedControl();
                break;
            case InputAction.Back:
                if (_navigation.Current == Route.Dashboard)
                {
                    Logout();
                }
                else
                {
                    _navigation.GoBack();
                }
                break;
        }
    }

    private static void MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement focused)
        {
            focused.MoveFocus(new TraversalRequest(direction));
        }
    }

    private static void ActivateFocusedControl()
    {
        if (Keyboard.FocusedElement is Button button && button.IsEnabled)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }
    }

    private void FocusFirstButton()
    {
        var firstButton = FindVisualChildren<Button>(RouteHost)
            .FirstOrDefault(button => button.IsVisible && button.IsEnabled && button.Focusable);

        firstButton?.Focus();
    }

    private void UpdateControllerStatus(ControllerConnectionEventArgs change)
    {
        _controllers[change.ControllerIndex] = change.IsConnected;
        var connected = _controllers
            .Select((isConnected, index) => (isConnected, index))
            .Where(item => item.isConnected)
            .Select(item => $"Controller {item.index + 1}")
            .ToArray();

        ControllerStatusText.Text = connected.Length == 0
            ? "No controllers"
            : string.Join("  •  ", connected);
    }

    private void BringGrevHomeToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

        _navigation.Reset(_session.PrimaryUser is null ? Route.Login : Route.Dashboard);

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
