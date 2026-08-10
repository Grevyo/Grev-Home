using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Sessions;
using GrevHome.Storage;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow : Window
{
    private readonly NavigationService _navigation = new();
    private readonly SessionContext _session = new();
    private readonly ControllerInputService _controllerInput = new();
    private readonly AppPaths _paths = new();
    private readonly ProfileService _profileService;
    private readonly bool[] _controllers = new bool[4];
    private readonly LoginView _loginView = new();
    private readonly CreateProfileView _createProfileView = new();
    private readonly DashboardView _dashboardView = new();
    private IReadOnlyList<LocalProfile> _profiles = Array.Empty<LocalProfile>();

    public MainWindow()
    {
        InitializeComponent();
        _profileService = new ProfileService(_paths);

        _navigation.RouteChanged += route => Dispatcher.Invoke(() => ShowRoute(route));
        _session.Changed += (_, _) => Dispatcher.Invoke(RefreshSessionSurfaces);

        _loginView.LocalProfileSignInRequested += SignInLocal;
        _loginView.GuestSignInRequested += controllerIndex => _session.SignInGuest(controllerIndex);
        _loginView.PrimaryUserRequested += sessionUserId => _session.SetPrimary(sessionUserId);
        _loginView.CreateProfileRequested += (_, _) => OpenCreateProfile();
        _loginView.EnterHomeRequested += (_, _) => EnterHome();
        _loginView.ClearSessionRequested += (_, _) => _session.SignOutAll();

        _createProfileView.CreateRequested += name => _ = CreateProfileAsync(name);
        _createProfileView.CancelRequested += (_, _) => ReturnToLogin();
        _dashboardView.ManageUsersRequested += (_, _) => OpenSessionLobby();
        _dashboardView.LogoutRequested += (_, _) => Logout();

        _controllerInput.ActionPressed += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleInput(input.Action, input.ControllerIndex)));
        _controllerInput.ConnectionChanged += change =>
            Dispatcher.BeginInvoke(new Action(() => UpdateControllerStatus(change)));
        _controllerInput.ReturnHomeRequested += _ =>
            Dispatcher.BeginInvoke(new Action(BringGrevHomeToFront));
        _controllerInput.Start();

        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => _controllerInput.Dispose();
    }

    private async Task InitializeAsync()
    {
        _paths.EnsureMachineLayout();
        _profiles = await _profileService.GetProfilesAsync();
        RefreshSessionSurfaces();
        _navigation.Reset(Route.Login);
        FocusFirstButton();
    }

    private void SignInLocal(ProfileSignInRequest request)
    {
        _session.SignInLocal(request.Profile, request.ControllerIndex);
    }

    private void EnterHome()
    {
        if (!_session.HasSignedInUsers)
        {
            return;
        }

        _dashboardView.SetSession(_session);
        _navigation.Navigate(Route.Dashboard);
    }

    private void OpenSessionLobby()
    {
        RefreshSessionSurfaces();
        _navigation.Reset(Route.Login);
    }

    private void OpenCreateProfile()
    {
        _createProfileView.Reset();
        _navigation.Navigate(Route.CreateProfile);
    }

    private async Task CreateProfileAsync(string displayName)
    {
        try
        {
            await _profileService.CreateAsync(displayName);
            _profiles = await _profileService.GetProfilesAsync();
            RefreshSessionSurfaces();
            _navigation.Reset(Route.Login);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _createProfileView.ShowError(ex.Message);
        }
    }

    private void ReturnToLogin()
    {
        _navigation.Reset(Route.Login);
        RefreshSessionSurfaces();
    }

    private void Logout()
    {
        _session.SignOutAll();
        _navigation.Reset(Route.Login);
    }

    private void RefreshSessionSurfaces()
    {
        _loginView.Refresh(_profiles, _session, _controllers);
        _dashboardView.SetSession(_session);
        UpdateControllerHeader();
    }

    private void ShowRoute(Route route)
    {
        RouteHost.Content = route switch
        {
            Route.Login => _loginView,
            Route.CreateProfile => _createProfileView,
            Route.Dashboard => _dashboardView,
            _ => _loginView
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox && e.Key != Key.Escape)
        {
            return;
        }

        var action = e.Key switch
        {
            Key.Up => InputAction.Up,
            Key.Down => InputAction.Down,
            Key.Left => InputAction.Left,
            Key.Right => InputAction.Right,
            Key.Enter or Key.Space => InputAction.Accept,
            Key.Escape => InputAction.Back,
            _ => (InputAction?)null
        };

        if (action is null)
        {
            return;
        }

        HandleInput(action.Value, controllerIndex: null);
        e.Handled = true;
    }

    private void HandleInput(InputAction action, int? controllerIndex)
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
                ActivateFocusedControl(controllerIndex);
                break;
            case InputAction.Back:
                HandleBack();
                break;
        }
    }

    private void HandleBack()
    {
        switch (_navigation.Current)
        {
            case Route.Dashboard:
                Logout();
                break;
            case Route.CreateProfile:
                ReturnToLogin();
                break;
            case Route.Login:
                break;
            default:
                _navigation.GoBack();
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

    private void ActivateFocusedControl(int? controllerIndex)
    {
        if (Keyboard.FocusedElement is not Button button || !button.IsEnabled)
        {
            return;
        }

        _loginView.ActivationControllerIndex = controllerIndex;
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }
        finally
        {
            _loginView.ActivationControllerIndex = null;
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
        RefreshSessionSurfaces();
    }

    private void UpdateControllerHeader()
    {
        var connected = _controllers
            .Select((isConnected, index) => (isConnected, index))
            .Where(item => item.isConnected)
            .Select(item =>
            {
                var user = _session.GetUserForController(item.index);
                return $"Controller {item.index + 1}{(user is null ? string.Empty : $" → {user.DisplayName}")}";
            })
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

        _navigation.Reset(_session.HasSignedInUsers ? Route.Dashboard : Route.Login);

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
