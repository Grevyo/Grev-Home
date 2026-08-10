using System.IO;
using System.Windows;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Sessions;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ProfilePlayersView _profilePlayersView = new();
    private readonly ProfileView _profileView = new();
    private readonly ProfileEditView _profileEditView = new();
    private string? _profileTargetGrevId;
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

        _profilePlayersView.ViewProfileRequested += OpenProfileView;
        _profilePlayersView.EditProfileRequested += OpenProfileEditor;
        _profilePlayersView.AddPlayerRequested += (_, _) => OpenAdditionalPlayerLogin();
        _profilePlayersView.LogoutRequested += (_, _) => Logout();
        _profilePlayersView.SignOutPlayerRequested += SignOutPlayer;
        _profilePlayersView.SetPrimaryRequested += SetPrimaryFromProfileMenu;
        _profilePlayersView.AssignControllerRequested += AssignControllerFromProfileMenu;

        _profileView.EditProfileRequested += (_, _) => OpenProfileEditorForTarget();
        _profileEditView.SaveRequested += request => _ = SaveProfileEditAsync(request);
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
                RenderProfileTarget();
                RouteHost.Content = _profileView;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
                break;
            case Route.ProfileEdit:
                RenderProfileEditor();
                RouteHost.Content = _profileEditView;
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

        _profilePlayersView.SetState(_session, _controllers, _profiles);
        if (_navigation.Current == Route.ProfileView)
        {
            RenderProfileTarget();
        }
        else if (_navigation.Current == Route.ProfileEdit)
        {
            RenderProfileEditor();
        }
    }

    private void ShellProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.HasSignedInUsers)
        {
            return;
        }

        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
        RefreshProfilePlayerViews();
        _navigation.Navigate(Route.ProfilePlayers);
    }

    private void OpenProfileView(Guid sessionUserId)
    {
        var user = FindSessionUser(sessionUserId);
        if (user?.GrevId is null)
        {
            return;
        }

        _profileTargetGrevId = user.GrevId;
        RenderProfileTarget();
        _navigation.Navigate(Route.ProfileView);
    }

    private void OpenProfileEditor(Guid sessionUserId)
    {
        var user = FindSessionUser(sessionUserId);
        if (user?.GrevId is null)
        {
            return;
        }

        _profileTargetGrevId = user.GrevId;
        OpenProfileEditorForTarget();
    }

    private void OpenProfileEditorForTarget()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        if (profile is null || actor?.GrevId is null ||
            !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId))
        {
            return;
        }

        RenderProfileEditor();
        _navigation.Navigate(Route.ProfileEdit);
    }

    private void OpenAdditionalPlayerLogin()
    {
        var actor = _session.PrimaryUser;
        if (actor is null || !AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers))
        {
            return;
        }

        OpenSessionLobby();
    }

    private void SignOutPlayer(Guid sessionUserId)
    {
        var actor = _session.PrimaryUser;
        var target = FindSessionUser(sessionUserId);
        if (actor is null || target is null)
        {
            return;
        }

        var canManageOthers = AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers);
        if (actor.SessionId != target.SessionId && !canManageOthers)
        {
            return;
        }

        _session.SignOut(sessionUserId);
        if (!_session.HasSignedInUsers)
        {
            _profileTargetGrevId = null;
            _navigation.Reset(Route.Login);
            return;
        }

        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
        RefreshProfilePlayerViews();
        _navigation.Reset(Route.Dashboard);
    }

    private void SetPrimaryFromProfileMenu(Guid sessionUserId)
    {
        var actor = _session.PrimaryUser;
        if (actor is null || !AccountAuthorizationService.Allows(actor.Role, AccountPermission.ChangePrimaryUser))
        {
            return;
        }

        _session.SetPrimary(sessionUserId);
        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
        RefreshProfilePlayerViews();
    }

    private void AssignControllerFromProfileMenu(PlayerControllerAssignmentRequest request)
    {
        var actor = _session.PrimaryUser;
        var target = FindSessionUser(request.SessionUserId);
        if (actor is null || target is null ||
            !AccountAuthorizationService.Allows(actor.Role, AccountPermission.AssignControllers))
        {
            return;
        }

        var canManageOthers = AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers);
        if (actor.SessionId != target.SessionId && !canManageOthers)
        {
            return;
        }

        _session.AssignController(request.ControllerIndex, request.SessionUserId);
        RefreshProfilePlayerViews();
    }

    private async Task SaveProfileEditAsync(ProfileEditRequest request)
    {
        var actor = _session.PrimaryUser;
        var profile = _profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.GrevId, request.GrevId, StringComparison.OrdinalIgnoreCase));
        if (actor?.GrevId is null || profile is null ||
            !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId))
        {
            _profileEditView.ShowStatus("The current Primary User is not allowed to edit that profile.");
            return;
        }

        var canChangeRole = AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManageRoles);
        var requestedRole = canChangeRole ? request.Role : profile.Role;

        try
        {
            var updated = await _profileService.UpdateProfileAsync(
                profile.GrevId,
                request.DisplayName,
                request.AvatarKey,
                requestedRole);
            _profiles = await _profileService.GetProfilesAsync();
            _session.UpdateDisplayName(updated.GrevId, updated.DisplayName);
            _session.UpdateRole(updated.GrevId, updated.Role);
            _profileTargetGrevId = updated.GrevId;
            RenderProfileEditor();
            _profileEditView.ShowStatus($"Saved {updated.DisplayName}. Username @{updated.Username} and GrevID were not changed.");
            RefreshSessionSurfaces();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _profileEditView.ShowStatus(ex.Message);
        }
    }

    private void RenderProfileTarget()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        var targetSessionUser = profile is null
            ? null
            : _session.SignedInUsers.FirstOrDefault(user =>
                string.Equals(user.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));

        var sessionStatus = targetSessionUser is null
            ? "Not currently signed in"
            : BuildProfileSessionStatus(targetSessionUser);
        var canEdit = profile is not null && actor?.GrevId is not null &&
                      AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId);
        _profileView.SetProfile(profile, sessionStatus, canEdit);
    }

    private void RenderProfileEditor()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        if (profile is null || actor?.GrevId is null)
        {
            return;
        }

        var canEdit = AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId);
        if (!canEdit)
        {
            return;
        }

        var canChangeRole = AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManageRoles);
        _profileEditView.SetProfile(profile, canChangeRole);
    }

    private string BuildProfileSessionStatus(SessionUser user)
    {
        var controllers = _session.GetControllersForUser(user.SessionId);
        var controllerText = controllers.Count == 0
            ? "No controller"
            : string.Join(", ", controllers.Select(index => $"Controller {index + 1}"));
        return $"Signed in  •  {controllerText}{(user.IsPrimary ? "  •  Primary User" : string.Empty)}";
    }

    private LocalProfile? GetProfileTarget()
    {
        var grevId = _profileTargetGrevId ?? _session.PrimaryUser?.GrevId;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return null;
        }

        return _profiles.FirstOrDefault(profile =>
            string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
    }

    private SessionUser? FindSessionUser(Guid sessionUserId) =>
        _session.SignedInUsers.FirstOrDefault(user => user.SessionId == sessionUserId);
}
