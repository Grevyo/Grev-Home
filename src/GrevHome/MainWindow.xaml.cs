using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Runtime;
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
    private readonly AppCatalogService _appCatalogue;
    private readonly AppPathResolver _appPathResolver;
    private readonly InstalledAppService _installedApps;
    private readonly RuntimeSessionManager _runtimeSessions;
    private readonly GrevOverlayWindow _overlayWindow;
    private readonly bool[] _controllers = new bool[4];
    private readonly LoginView _loginView = new();
    private readonly CreateProfileView _createProfileView = new();
    private readonly DashboardView _dashboardView = new();
    private readonly InstalledLibraryView _installedLibraryView = new();
    private readonly RunningAppsView _runningAppsView = new();
    private readonly AppKillerView _appKillerView = new();
    private IReadOnlyList<LocalProfile> _profiles = Array.Empty<LocalProfile>();
    private Guid? _foregroundLaunchSessionId;

    public MainWindow()
    {
        InitializeComponent();
        _profileService = new ProfileService(_paths);
        _appCatalogue = new AppCatalogService(_paths);
        _appPathResolver = new AppPathResolver(_paths);
        _installedApps = new InstalledAppService(_paths, _appPathResolver, _appCatalogue);
        _runtimeSessions = new RuntimeSessionManager(
            new ProcessTreeService(),
            new ProcessWindowService(),
            new PlaytimeService(_paths),
            new AppLaunchResolver());
        _overlayWindow = new GrevOverlayWindow();

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
        _dashboardView.InstalledAppsRequested += (_, _) => _ = OpenInstalledLibraryAsync();
        _dashboardView.RunningAppsRequested += (_, _) => OpenRunningApps();
        _dashboardView.AppKillerRequested += (_, _) => OpenAppKiller();
        _dashboardView.LogoutRequested += (_, _) => Logout();

        _installedLibraryView.BackRequested += (_, _) => _navigation.GoBack();
        _installedLibraryView.LaunchRequested += entry => _ = LaunchInstalledAppAsync(entry);

        _runningAppsView.BackRequested += (_, _) => _navigation.GoBack();
        _runningAppsView.SwitchRequested += SwitchToSession;
        _runningAppsView.CloseRequested += RequestCloseSession;

        _appKillerView.BackRequested += (_, _) => _navigation.GoBack();
        _appKillerView.SwitchRequested += SwitchToSession;
        _appKillerView.CloseRequested += RequestCloseSession;
        _appKillerView.ForceCloseRequested += ForceCloseSession;

        _overlayWindow.ResumeRequested += SwitchToSession;
        _overlayWindow.SwitchRequested += SwitchToSession;
        _overlayWindow.CloseRequested += launchSessionId =>
        {
            RequestCloseSession(launchSessionId);
            BringGrevHomeToFront();
        };
        _overlayWindow.ReturnHomeRequested += (_, _) => BringGrevHomeToFront();
        _overlayWindow.RunningAppsRequested += (_, _) =>
        {
            OpenRunningApps();
            RestoreWindowWithoutChangingRoute();
        };
        _overlayWindow.AppKillerRequested += (_, _) =>
        {
            OpenAppKiller();
            RestoreWindowWithoutChangingRoute();
        };

        _runtimeSessions.SessionChanged += _ =>
            Dispatcher.BeginInvoke(new Action(UpdateRuntimeSurfaces));
        _runtimeSessions.SessionEnded += snapshot =>
            Dispatcher.BeginInvoke(new Action(() => HandleRuntimeSessionEnded(snapshot)));

        _controllerInput.ActionPressed += input =>
            Dispatcher.BeginInvoke(new Action(() => HandleInput(input.Action, input.ControllerIndex)));
        _controllerInput.ConnectionChanged += change =>
            Dispatcher.BeginInvoke(new Action(() => UpdateControllerStatus(change)));
        _controllerInput.ReturnHomeRequested += _ =>
            Dispatcher.BeginInvoke(new Action(BringGrevHomeToFront));
        _controllerInput.OverlayRequested += _ =>
            Dispatcher.BeginInvoke(new Action(OpenOverlay));
        _controllerInput.Start();

        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _overlayWindow.Dismiss();
            _overlayWindow.Close();
            _controllerInput.Dispose();
            _runtimeSessions.Dispose();
        };
    }

    private async Task InitializeAsync()
    {
        _paths.EnsureMachineLayout();
        _profiles = await _profileService.GetProfilesAsync();
        RefreshSessionSurfaces();
        UpdateRuntimeSurfaces();
        _navigation.Reset(Route.Login);
        FocusFirstButton();
    }

    private void SignInLocal(ProfileSignInRequest request) =>
        _session.SignInLocal(request.Profile, request.ControllerIndex);

    private void EnterHome()
    {
        if (!_session.HasSignedInUsers)
        {
            return;
        }

        _dashboardView.SetSession(_session);
        _navigation.Navigate(Route.Dashboard);
    }

    private async Task OpenInstalledLibraryAsync()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        var primary = _session.PrimaryUser;
        var entries = await _installedApps.GetInstalledForUserAsync(primary?.GrevId);
        _installedLibraryView.SetLibrary(entries, primary);
        _navigation.Navigate(Route.InstalledLibrary);
    }

    private void OpenRunningApps()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        UpdateRuntimeSurfaces();
        _navigation.Navigate(Route.RunningApps);
    }

    private void OpenAppKiller()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        UpdateRuntimeSurfaces();
        _navigation.Navigate(Route.AppKiller);
    }

    private void OpenOverlay()
    {
        var active = _runtimeSessions.GetActiveSessions();
        var foreground = _runtimeSessions.GetForegroundSession();
        if (foreground is null && _foregroundLaunchSessionId.HasValue)
        {
            foreground = active.FirstOrDefault(session => session.LaunchSessionId == _foregroundLaunchSessionId.Value);
        }

        _overlayWindow.Open(active, foreground);
    }

    private async Task LaunchInstalledAppAsync(InstalledAppEntry entry)
    {
        try
        {
            var launched = await _runtimeSessions.LaunchAsync(entry, _session);
            _foregroundLaunchSessionId = launched.LaunchSessionId;
            _installedLibraryView.ShowLaunchStarted(launched);
            UpdateRuntimeSurfaces();
            Hide();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            _installedLibraryView.ShowLaunchError(ex.Message);
        }
    }

    private void SwitchToSession(Guid launchSessionId)
    {
        _overlayWindow.Dismiss();
        if (!_runtimeSessions.SwitchTo(launchSessionId))
        {
            RestoreWindowWithoutChangingRoute();
            return;
        }

        _foregroundLaunchSessionId = launchSessionId;
        Hide();
    }

    private void RequestCloseSession(Guid launchSessionId)
    {
        _ = _runtimeSessions.RequestClose(launchSessionId);
        UpdateRuntimeSurfaces();
    }

    private void ForceCloseSession(Guid launchSessionId)
    {
        _ = _runtimeSessions.ForceClose(launchSessionId);
        UpdateRuntimeSurfaces();
    }

    private void HandleRuntimeSessionEnded(LaunchSessionSnapshot snapshot)
    {
        UpdateRuntimeSurfaces();

        if (_foregroundLaunchSessionId != snapshot.LaunchSessionId)
        {
            return;
        }

        _foregroundLaunchSessionId = null;
        _overlayWindow.Dismiss();
        RestoreWindowWithoutChangingRoute();
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

    private async Task CreateProfileAsync(string username)
    {
        try
        {
            await _profileService.CreateAsync(username);
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

    private void UpdateRuntimeSurfaces()
    {
        var active = _runtimeSessions.GetActiveSessions();
        _dashboardView.SetRunningCount(active.Count);
        _runningAppsView.SetSessions(active);
        _appKillerView.SetSessions(active);
        _overlayWindow.Refresh(active);
    }

    private void ShowRoute(Route route)
    {
        RouteHost.Content = route switch
        {
            Route.Login => _loginView,
            Route.CreateProfile => _createProfileView,
            Route.Dashboard => _dashboardView,
            Route.InstalledLibrary => _installedLibraryView,
            Route.RunningApps => _runningAppsView,
            Route.AppKiller => _appKillerView,
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
        if (_overlayWindow.IsOpen)
        {
            _overlayWindow.HandleControllerInput(action);
            return;
        }

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
        _overlayWindow.Dismiss();
        _foregroundLaunchSessionId = null;
        _navigation.Reset(_session.HasSignedInUsers ? Route.Dashboard : Route.Login);
        RestoreWindowWithoutChangingRoute();
    }

    private void RestoreWindowWithoutChangingRoute()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

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
