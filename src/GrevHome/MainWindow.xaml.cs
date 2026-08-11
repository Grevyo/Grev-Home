using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private readonly AppPaths _paths = new();
    private readonly ControllerShortcutService _controllerShortcuts;
    private readonly ControllerInputService _controllerInput;
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
    private readonly SettingsView _settingsView = new();
    private IReadOnlyList<LocalProfile> _profiles = Array.Empty<LocalProfile>();
    private Guid? _foregroundLaunchSessionId;
    private ShortcutRecordRequest? _pendingShortcutRecord;

    public MainWindow()
    {
        InitializeComponent();
        _controllerShortcuts = new ControllerShortcutService(_paths);
        _controllerInput = new ControllerInputService(_controllerShortcuts);
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

        _createProfileView.CreateRequested += request => _ = CreateProfileAsync(request);
        _createProfileView.CancelRequested += (_, _) => ReturnToLogin();
        _dashboardView.ManageUsersRequested += (_, _) => OpenSessionLobby();
        _dashboardView.InstalledAppsRequested += (_, _) => _ = OpenInstalledLibraryAsync();
        _dashboardView.RunningAppsRequested += (_, _) => OpenRunningApps();
        _dashboardView.AppKillerRequested += (_, _) => OpenAppKiller();
        _dashboardView.SettingsRequested += (_, _) => OpenSettings();
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

        _settingsView.BackRequested += (_, _) => CloseSettings();
        _settingsView.SaveDisplayNameRequested += displayName => _ = SaveDisplayNameAsync(displayName);
        _settingsView.RecordShortcutRequested += BeginShortcutRecording;
        _settingsView.RemoveShortcutRequested += RemoveShortcut;
        _settingsView.AdjustShortcutHoldRequested += AdjustShortcutHold;
        _settingsView.ResetShortcutsRequested += (_, _) => ResetShortcuts();
        _settingsView.CancelShortcutCaptureRequested += (_, _) => CancelShortcutRecording();

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
        _controllerInput.ShortcutRequested += shortcut =>
            Dispatcher.BeginInvoke(new Action(() => HandleSystemShortcut(shortcut)));
        _controllerInput.ShortcutCaptured += capture =>
            Dispatcher.BeginInvoke(new Action(() => CompleteShortcutRecording(capture)));
        _controllerInput.ShortcutCaptureTimedOut += () =>
            Dispatcher.BeginInvoke(new Action(ShortcutRecordingTimedOut));
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
        _navigation.Reset(Route.Dashboard);
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

    private void OpenSettings()
    {
        RefreshSettingsState();
        _navigation.Navigate(Route.Settings);
    }

    private void CloseSettings()
    {
        CancelShortcutRecording(showMessage: false);
        _navigation.GoBack();
    }

    private void RefreshSettingsState()
    {
        _settingsView.SetState(GetPrimaryLocalProfile(), _controllerShortcuts.LoadOrCreate());
    }

    private LocalProfile? GetPrimaryLocalProfile()
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return null;
        }

        return _profiles.FirstOrDefault(profile =>
            string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SaveDisplayNameAsync(string displayName)
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            _settingsView.ShowAccountStatus("A local Primary User is required to edit Display Name.");
            return;
        }

        try
        {
            var updated = await _profileService.UpdateDisplayNameAsync(profile.GrevId, displayName);
            _profiles = await _profileService.GetProfilesAsync();
            _session.UpdateDisplayName(updated.GrevId, updated.DisplayName);
            RefreshSettingsState();
            _settingsView.ShowAccountStatus(
                $"Display Name changed to {updated.DisplayName}. Username @{updated.Username} and GrevID {updated.GrevId} were not changed.",
                closeEditor: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _settingsView.ShowAccountStatus(ex.Message);
        }
    }

    private void BeginShortcutRecording(ShortcutRecordRequest request)
    {
        _pendingShortcutRecord = request;
        _settingsView.BeginCapture(request.Action, request.ExistingBindingId is not null);
        _controllerInput.BeginShortcutCapture();
    }

    private void CompleteShortcutRecording(ControllerShortcutCaptureEventArgs capture)
    {
        var request = _pendingShortcutRecord;
        _pendingShortcutRecord = null;
        if (request is null)
        {
            return;
        }

        var configuration = _controllerShortcuts.LoadOrCreate();
        var bindings = configuration.Bindings.ToList();

        if (request.ExistingBindingId is not null)
        {
            var index = bindings.FindIndex(binding =>
                string.Equals(binding.Id, request.ExistingBindingId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                _settingsView.EndCapture("That shortcut no longer exists. Nothing was changed.");
                return;
            }

            bindings[index] = bindings[index] with { Buttons = capture.Buttons };
        }
        else
        {
            var prefix = request.Action == ControllerShortcutAction.ReturnHome ? "return-home" : "overlay";
            var hold = request.Action == ControllerShortcutAction.ReturnHome ? 700 : 450;
            bindings.Add(new ControllerShortcutBinding(
                $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)],
                request.Action,
                capture.Buttons,
                hold));
        }

        SaveShortcutConfiguration(
            new ControllerShortcutConfiguration(configuration.Version, bindings),
            $"Saved {SettingsView.FormatButtons(capture.Buttons)} from Controller {capture.ControllerIndex + 1}.");
        _settingsView.EndCapture(ShortcutStatusMessage);
    }

    private string ShortcutStatusMessage { get; set; } = string.Empty;

    private void RemoveShortcut(string bindingId)
    {
        var configuration = _controllerShortcuts.LoadOrCreate();
        var bindings = configuration.Bindings
            .Where(binding => !string.Equals(binding.Id, bindingId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (bindings.Length == configuration.Bindings.Count)
        {
            _settingsView.ShowShortcutStatus("That shortcut no longer exists.");
            return;
        }

        SaveShortcutConfiguration(
            new ControllerShortcutConfiguration(configuration.Version, bindings),
            "Shortcut removed.");
    }

    private void AdjustShortcutHold(ShortcutHoldAdjustment adjustment)
    {
        var configuration = _controllerShortcuts.LoadOrCreate();
        var bindings = configuration.Bindings.ToList();
        var index = bindings.FindIndex(binding =>
            string.Equals(binding.Id, adjustment.BindingId, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            _settingsView.ShowShortcutStatus("That shortcut no longer exists.");
            return;
        }

        var newHold = Math.Clamp(bindings[index].HoldMilliseconds + adjustment.DeltaMilliseconds, 0, 5000);
        bindings[index] = bindings[index] with { HoldMilliseconds = newHold };

        SaveShortcutConfiguration(
            new ControllerShortcutConfiguration(configuration.Version, bindings),
            $"Hold time changed to {newHold} ms.");
    }

    private void ResetShortcuts()
    {
        SaveShortcutConfiguration(
            ControllerShortcutService.CreateDefaults(),
            "Controller system shortcuts reset to the Grev Home defaults.");
    }

    private void SaveShortcutConfiguration(ControllerShortcutConfiguration configuration, string successMessage)
    {
        try
        {
            _controllerShortcuts.Save(configuration);
            _controllerInput.ReloadShortcuts();
            RefreshSettingsState();
            ShortcutStatusMessage = successMessage;
            _settingsView.ShowShortcutStatus(successMessage);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShortcutStatusMessage = ex.Message;
            _settingsView.ShowShortcutStatus(ex.Message);
        }
    }

    private void CancelShortcutRecording(bool showMessage = true)
    {
        if (!_controllerInput.IsCapturingShortcut && _pendingShortcutRecord is null)
        {
            return;
        }

        _controllerInput.CancelShortcutCapture();
        _pendingShortcutRecord = null;
        if (showMessage)
        {
            _settingsView.EndCapture("Shortcut recording cancelled.");
        }
    }

    private void ShortcutRecordingTimedOut()
    {
        _pendingShortcutRecord = null;
        _settingsView.EndCapture("No combination was recorded. Recording timed out after 15 seconds.");
    }

    private void HandleSystemShortcut(ControllerShortcutEventArgs shortcut)
    {
        if (IsStoreModalOpen || IsPowerMenuOpen)
        {
            return;
        }

        switch (shortcut.Action)
        {
            case ControllerShortcutAction.ReturnHome:
                BringGrevHomeToFront();
                break;
            case ControllerShortcutAction.Overlay:
                OpenOverlay();
                break;
        }
    }

    private void OpenOverlay()
    {
        if (IsStoreModalOpen || IsPowerMenuOpen)
        {
            return;
        }

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
        _navigation.Navigate(Route.Login);
    }

    private void OpenCreateProfile()
    {
        _createProfileView.Reset();
        _navigation.Navigate(Route.CreateProfile);
    }

    private async Task CreateProfileAsync(CreateProfileRequest request)
    {
        try
        {
            await _profileService.CreateAsync(request.Username, request.Role);
            _profiles = await _profileService.GetProfilesAsync();
            RefreshSessionSurfaces();
            ReturnToLogin();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _createProfileView.ShowError(ex.Message);
        }
    }

    private void ReturnToLogin()
    {
        if (!_navigation.GoBack())
        {
            _navigation.Reset(Route.Login);
        }

        RefreshSessionSurfaces();
    }

    private void Logout()
    {
        CancelShortcutRecording(showMessage: false);
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
            Route.Settings => _settingsView,
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
        if (_storeInstallBusy)
        {
            return;
        }

        if (IsPowerMenuOpen)
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
                    ClosePowerMenu();
                    break;
            }
            return;
        }

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
                break;
            case Route.CreateProfile:
                ReturnToLogin();
                break;
            case Route.Settings:
                CloseSettings();
                break;
            case Route.Login:
                if (_session.HasSignedInUsers)
                {
                    _navigation.Reset(Route.Dashboard);
                }
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
        ProfileBubbleButton.Visibility = _session.HasSignedInUsers
            ? Visibility.Visible
            : Visibility.Collapsed;
        HeaderPlayersPanel.Children.Clear();

        if (!_session.HasSignedInUsers)
        {
            return;
        }

        foreach (var user in _session.SignedInUsers)
        {
            HeaderPlayersPanel.Children.Add(CreateHeaderPlayerBadge(user));
        }
    }

    private UIElement CreateHeaderPlayerBadge(SessionUser user)
    {
        var profile = string.IsNullOrWhiteSpace(user.GrevId)
            ? null
            : _profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.GrevId, user.GrevId, StringComparison.OrdinalIgnoreCase));
        var assignedControllers = _session.GetControllersForUser(user.SessionId);
        var hasAssignedController = assignedControllers.Count > 0;
        var hasConnectedAssignedController = assignedControllers.Any(index =>
            index >= 0 && index < _controllers.Length && _controllers[index]);
        var roleBrush = GetProfileRoleBrush(user.Role);

        var controllerHost = new Grid
        {
            Width = 26,
            Height = 32,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        controllerHost.Children.Add(new TextBlock
        {
            Text = "🎮",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = hasConnectedAssignedController
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(91, 98, 112)),
            Opacity = hasConnectedAssignedController ? 1d : 0.55d
        });

        if (!hasAssignedController)
        {
            controllerHost.Children.Add(new TextBlock
            {
                Text = "╳",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(224, 82, 94)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var content = new Grid
        {
            Height = 34,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        content.Children.Add(controllerHost);

        var avatar = CreateHeaderAvatar(profile, user, 32, roleBrush);
        Grid.SetColumn(avatar, 1);
        content.Children.Add(avatar);

        var displayName = new TextBlock
        {
            Text = user.DisplayName,
            MaxWidth = 128,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetColumn(displayName, 2);
        content.Children.Add(displayName);

        return new Border
        {
            MinWidth = 142,
            MaxWidth = 202,
            Height = 42,
            Padding = new Thickness(7, 3, 9, 3),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(17),
            Background = user.IsPrimary
                ? new SolidColorBrush(Color.FromRgb(31, 40, 58))
                : new SolidColorBrush(Color.FromRgb(18, 23, 33)),
            BorderBrush = roleBrush,
            BorderThickness = new Thickness(1.5),
            Effect = CreateHeaderRoleEffect(user.Role, roleBrush.Color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = content
        };
    }

    private Border CreateHeaderAvatar(LocalProfile? profile, SessionUser user, double size, SolidColorBrush roleBrush)
    {
        var imageSource = profile is null ? null : ProfileAvatarCatalog.TryLoadCustomImage(profile);
        var host = new Grid();

        if (imageSource is not null)
        {
            host.Children.Add(new Image
            {
                Source = imageSource,
                Stretch = Stretch.UniformToFill,
                Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2)
            });
        }
        else
        {
            host.Children.Add(new TextBlock
            {
                Text = profile is null
                    ? ProfileAvatarCatalog.GetDisplayGlyph(ProfileAvatarCatalog.DefaultKey, user.DisplayName)
                    : ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            BorderBrush = roleBrush,
            BorderThickness = new Thickness(1.25),
            ClipToBounds = true,
            Child = host
        };
    }

    private SolidColorBrush GetProfileRoleBrush(AccountRole role) =>
        (SolidColorBrush)FindResource(role switch
        {
            AccountRole.Admin => "AdminRoleBrush",
            AccountRole.Standard => "StandardRoleBrush",
            _ => "GuestRoleBrush"
        });

    private static DropShadowEffect? CreateHeaderRoleEffect(AccountRole role, Color color) => role switch
    {
        AccountRole.Admin => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 14,
            ShadowDepth = 0,
            Opacity = 0.58
        },
        AccountRole.Standard => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 7,
            ShadowDepth = 0,
            Opacity = 0.18
        },
        _ => null
    };

    private void ShellBack_Click(object sender, RoutedEventArgs e) => HandleBack();

    private void ShellProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_session.HasSignedInUsers)
        {
            OpenSessionLobby();
        }
    }

    private void ShellSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void BringGrevHomeToFront()
    {
        if (IsStoreModalOpen || IsPowerMenuOpen)
        {
            return;
        }

        CancelShortcutRecording(showMessage: false);
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
