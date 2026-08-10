using System.Windows;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ProfilePlayersView _profilePlayersView = new();
    private readonly ProfileView _profileView = new();
    private bool _profilePlayersIntegrationReady;

    private void InitializeProfilePlayersIntegration()
    {
        InitializeRuntimeRecoveryIntegration();

        if (_profilePlayersIntegrationReady)
        {
            return;
        }

        _profilePlayersIntegrationReady = true;
        _navigation.RouteChanged += HandleProfileRouteChanged;
        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshProfilePlayerViews));
        _controllerInput.ConnectionChanged += _ => Dispatcher.BeginInvoke(new Action(RefreshProfilePlayerViews));

        _profilePlayersView.ViewProfileRequested += (_, _) => OpenProfileView();
        _profilePlayersView.EditProfileRequested += (_, _) => OpenProfileEditorFromMenu();
        _profilePlayersView.AddPlayerRequested += (_, _) => OpenSessionLobby();
        _profilePlayersView.LogoutRequested += (_, _) => Logout();
        _profilePlayersView.SetPrimaryRequested += sessionUserId =>
        {
            _session.SetPrimary(sessionUserId);
            RefreshProfilePlayerViews();
        };
        _profilePlayersView.AssignControllerRequested += request =>
        {
            _session.AssignController(request.ControllerIndex, request.SessionUserId);
            RefreshProfilePlayerViews();
        };

        _profileView.EditProfileRequested += (_, _) => OpenProfileEditorFromMenu();
    }

    private void HandleProfileRouteChanged(Route route)
    {
        ShellBackButton.IsEnabled = route != Route.Dashboard &&
                                    !(route == Route.Login && !_session.HasSignedInUsers);

        switch (route)
        {
            case Route.ProfilePlayers:
                RefreshProfilePlayerViews();
                RouteHost.Content = _profilePlayersView;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
                break;
            case Route.ProfileView:
                _profileView.SetProfile(GetPrimaryLocalProfile());
                RouteHost.Content = _profileView;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
                break;
        }
    }

    private void RefreshProfilePlayerViews()
    {
        if (!_profilePlayersIntegrationReady)
        {
            return;
        }

        _profilePlayersView.SetState(_session, _controllers);
        _profileView.SetProfile(GetPrimaryLocalProfile());
    }

    private void ShellProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.HasSignedInUsers)
        {
            return;
        }

        RefreshProfilePlayerViews();
        _navigation.Navigate(Route.ProfilePlayers);
    }

    private void OpenProfileView()
    {
        _profileView.SetProfile(GetPrimaryLocalProfile());
        _navigation.Navigate(Route.ProfileView);
    }

    private void OpenProfileEditorFromMenu()
    {
        RefreshSettingsState();
        _navigation.Navigate(Route.Settings);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(_settingsView.OpenProfileEditor));
    }
}
