using System.IO;
using System.Windows;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Runtime;
using GrevHome.Sessions;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ProfilePlayersView _profilePlayersView = new();
    private readonly ProfileView _profileView = new();
    private readonly ProfileEditView _profileEditView = new();
    private readonly ProfilePhotoPickerView _profilePhotoPickerView = new();
    private ProfileStatsService? _profileStatsService;
    private string? _profileTargetGrevId;
    private string? _profilePhotoCurrentPath;
    private ProfileEditRequest? _profileEditDraftBeforePhotoPicker;
    private Route? _profileKeyboardModalRoute;
    private bool _profilePlayersIntegrationReady;

    private void InitializeProfilePlayersIntegration()
    {
        if (_profilePlayersIntegrationReady) return;

        _profilePlayersIntegrationReady = true;
        _profileStatsService = new ProfileStatsService(new IProfileStatsSource[]
        {
            new GrevHomeProfileStatsSource(new PlaytimeService(_paths))
        });

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
        _profilePlayersView.UnassignControllerRequested += UnassignControllerFromProfileMenu;

        ProfileQuickMenu.ViewProfileRequested += OpenProfileViewFromQuickMenu;
        ProfileQuickMenu.SetPrimaryRequested += SetPrimaryFromProfileMenu;
        ProfileQuickMenu.SignOutPlayerRequested += SignOutPlayer;
        ProfileQuickMenu.AssignControllerRequested += AssignControllerFromProfileMenu;
        ProfileQuickMenu.UnassignControllerRequested += UnassignControllerFromProfileMenu;
        ProfileQuickMenu.AddPlayerRequested += (_, _) => OpenAdditionalPlayerLoginFromQuickMenu();
        ProfileQuickMenu.ManagePlayersRequested += (_, _) => OpenFullProfilePlayersFromQuickMenu();
        ProfileQuickMenu.CloseRequested += (_, _) => ClosePowerMenu();

        _profileView.EditProfileRequested += (_, _) => OpenProfileEditorForTarget();
        _profileEditView.SaveRequested += request => _ = SaveProfileEditAsync(request);
        _profileEditView.ChooseCustomPhotoRequested += (_, _) => OpenProfilePhotoPicker();
        _profileEditView.KeyboardOpened += (_, _) => ProfileKeyboardOpened(Route.ProfileEdit);
        _profileEditView.KeyboardClosed += (_, _) => ProfileKeyboardClosed(Route.ProfileEdit);
        _createProfileView.KeyboardOpened += (_, _) => ProfileKeyboardOpened(Route.CreateProfile);
        _createProfileView.KeyboardClosed += (_, _) => ProfileKeyboardClosed(Route.CreateProfile);

        _profilePhotoPickerView.HomeRequested += (_, _) => ShowProfilePhotoHome();
        _profilePhotoPickerView.UpRequested += (_, _) => NavigateProfilePhotoUp();
        _profilePhotoPickerView.CancelRequested += (_, _) => _navigation.GoBack();
        _profilePhotoPickerView.NavigateRequested += NavigateProfilePhotoPath;
        _profilePhotoPickerView.PhotoSelected += SelectProfilePhoto;
    }

    private void HandleProfileRouteChanged(Route route)
    {
        switch (route)
        {
            case Route.ProfilePlayers:
                RefreshProfilePlayerViews();
                RouteHost.Content = _profilePlayersView;
                break;
            case Route.ProfileView:
                RenderProfileTarget();
                RouteHost.Content = _profileView;
                break;
            case Route.ProfileEdit:
                RenderProfileEditor();
                if (_profileEditDraftBeforePhotoPicker is not null)
                {
                    _profileEditView.RestoreDraft(_profileEditDraftBeforePhotoPicker);
                    _profileEditDraftBeforePhotoPicker = null;
                }
                RouteHost.Content = _profileEditView;
                break;
            case Route.ProfilePhotoPicker:
                RouteHost.Content = _profilePhotoPickerView;
                break;
        }
    }

    private void FocusRouteSoon() => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));

    private void RefreshProfilePlayerViews()
    {
        if (!_profilePlayersIntegrationReady) return;
        _profilePlayersView.SetState(_session, _controllers, _profiles);
        if (PowerMenuOverlay.Visibility == Visibility.Visible && ProfileQuickMenuCard.Visibility == Visibility.Visible)
        {
            ProfileQuickMenu.SetState(_session, _controllers, _profiles);
        }
        if (_navigation.Current == Route.ProfileView) RenderProfileTarget();
        else if (_navigation.Current == Route.ProfileEdit && !_profileEditView.IsKeyboardOpen) RenderProfileEditor();
    }

    private void RefreshProfileQuickMenu()
    {
        if (!_profilePlayersIntegrationReady) return;
        ProfileQuickMenu.SetState(_session, _controllers, _profiles);
    }

    private void ShellProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.HasSignedInUsers || IsStoreModalOpen || IsPowerMenuOpen) return;

        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
        RefreshProfileQuickMenu();
        ResetHeaderPowerConfirmation();
        _headerFlyoutReturnButton = ProfileBubbleButton;
        PowerMenuCard.Visibility = Visibility.Collapsed;
        ProfileQuickMenuCard.Visibility = Visibility.Visible;
        ShellInteractionHost.IsEnabled = false;
        PowerMenuOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(ProfileQuickMenu.FocusInitial));
    }

    private void OpenFullProfilePlayersFromQuickMenu()
    {
        ClosePowerMenu(returnFocusToHeader: false);
        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
        RefreshProfilePlayerViews();
        _navigation.Navigate(Route.ProfilePlayers);
    }

    private void OpenAdditionalPlayerLoginFromQuickMenu()
    {
        ClosePowerMenu(returnFocusToHeader: false);
        OpenAdditionalPlayerLogin();
    }

    private void OpenProfileViewFromQuickMenu(Guid sessionUserId)
    {
        ClosePowerMenu(returnFocusToHeader: false);
        OpenProfileView(sessionUserId);
    }

    private void OpenProfileView(Guid sessionUserId)
    {
        var user = FindSessionUser(sessionUserId);
        if (user?.GrevId is null) return;
        _profileTargetGrevId = user.GrevId;
        RenderProfileTarget();
        _navigation.Navigate(Route.ProfileView);
    }

    private void OpenProfileEditor(Guid sessionUserId)
    {
        var user = FindSessionUser(sessionUserId);
        if (user?.GrevId is null) return;
        _profileTargetGrevId = user.GrevId;
        OpenProfileEditorForTarget();
    }

    private void OpenProfileEditorForTarget()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        if (profile is null || actor?.GrevId is null || !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId)) return;
        RenderProfileEditor();
        _navigation.Navigate(Route.ProfileEdit);
    }

    private void OpenAdditionalPlayerLogin()
    {
        var actor = _session.PrimaryUser;
        if (actor is null || !AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers)) return;
        OpenSessionLobby();
    }

    private void SignOutPlayer(Guid sessionUserId)
    {
        var actor = _session.PrimaryUser;
        var target = FindSessionUser(sessionUserId);
        if (actor is null || target is null) return;
        var canManageOthers = AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers);
        if (actor.SessionId != target.SessionId && !canManageOthers) return;

        _session.SignOut(sessionUserId);
        if (!_session.HasSignedInUsers)
        {
            ClosePowerMenu(returnFocusToHeader: false);
            _profileTargetGrevId = null;
            _navigation.Reset(Route.Login);
            return;
        }

        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
    }

    private void SetPrimaryFromProfileMenu(Guid sessionUserId)
    {
        var actor = _session.PrimaryUser;
        if (actor is null || !AccountAuthorizationService.Allows(actor.Role, AccountPermission.ChangePrimaryUser)) return;
        _session.SetPrimary(sessionUserId);
        _profileTargetGrevId = _session.PrimaryUser?.GrevId;
    }

    private void AssignControllerFromProfileMenu(PlayerControllerAssignmentRequest request)
    {
        var actor = _session.PrimaryUser;
        var target = FindSessionUser(request.SessionUserId);
        if (!CanManageController(actor, target)) return;
        _session.AssignController(request.ControllerIndex, request.SessionUserId);
    }

    private void UnassignControllerFromProfileMenu(PlayerControllerAssignmentRequest request)
    {
        var actor = _session.PrimaryUser;
        var target = FindSessionUser(request.SessionUserId);
        if (!CanManageController(actor, target)) return;
        _session.UnassignController(request.ControllerIndex, request.SessionUserId);
    }

    private static bool CanManageController(SessionUser? actor, SessionUser? target)
    {
        if (actor is null || target is null || !AccountAuthorizationService.Allows(actor.Role, AccountPermission.AssignControllers)) return false;
        return actor.SessionId == target.SessionId || AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers);
    }

    private async Task SaveProfileEditAsync(ProfileEditRequest request)
    {
        var actor = _session.PrimaryUser;
        var profile = _profiles.FirstOrDefault(candidate => string.Equals(candidate.GrevId, request.GrevId, StringComparison.OrdinalIgnoreCase));
        if (actor?.GrevId is null || profile is null || !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId))
        {
            _profileEditView.ShowStatus("The current Primary User is not allowed to edit that profile.");
            return;
        }

        var requestedRole = AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManageRoles) ? request.Role : profile.Role;
        try
        {
            var updated = await _profileService.UpdateProfileAsync(
                profile.GrevId,
                request.DisplayName,
                request.AvatarKey,
                requestedRole,
                customAvatarSourcePath: request.CustomAvatarSourcePath,
                bio: request.Bio,
                statusMessage: request.StatusMessage);
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

    private void OpenProfilePhotoPicker()
    {
        var draft = _profileEditView.CaptureDraft();
        if (draft is null) return;
        _profileEditDraftBeforePhotoPicker = draft;
        _profilePhotoCurrentPath = null;
        ShowProfilePhotoHome();
        _navigation.Navigate(Route.ProfilePhotoPicker);
    }

    private void ShowProfilePhotoHome()
    {
        _profilePhotoCurrentPath = null;
        try
        {
            var locations = _fileSystem.GetHomeLocations(_paths.Root)
                .Where(location => location.Name is not "Test Area" and not "Grev Home Data")
                .ToArray();
            _profilePhotoPickerView.ShowHome(locations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _profilePhotoPickerView.ShowError(ex.Message);
        }
    }

    private void NavigateProfilePhotoPath(string path)
    {
        try
        {
            var entries = _fileSystem.GetEntries(path);
            _profilePhotoCurrentPath = path;
            _profilePhotoPickerView.ShowDirectory(path, entries, _fileSystem.GetParent(path) is not null);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _profilePhotoPickerView.ShowError(ex.Message);
        }
    }

    private void NavigateProfilePhotoUp()
    {
        if (string.IsNullOrWhiteSpace(_profilePhotoCurrentPath))
        {
            ShowProfilePhotoHome();
            return;
        }
        var parent = _fileSystem.GetParent(_profilePhotoCurrentPath);
        if (parent is null) ShowProfilePhotoHome();
        else NavigateProfilePhotoPath(parent);
    }

    private void SelectProfilePhoto(string path)
    {
        if (_profileEditDraftBeforePhotoPicker is null) return;
        _profileEditDraftBeforePhotoPicker = _profileEditDraftBeforePhotoPicker with
        {
            AvatarKey = ProfileAvatarCatalog.CustomKey,
            CustomAvatarSourcePath = path
        };
        _navigation.GoBack();
        _profileEditView.ShowStatus($"Selected {Path.GetFileName(path)}. Save Profile to copy it into this GrevID.");
    }

    private void ProfileKeyboardOpened(Route route)
    {
        if (_navigation.Current != route || _profileKeyboardModalRoute.HasValue) return;
        _navigation.PushCurrentBackEntry();
        _profileKeyboardModalRoute = route;
    }

    private void ProfileKeyboardClosed(Route route)
    {
        if (_profileKeyboardModalRoute != route) return;
        _navigation.DiscardBackEntry(route);
        _profileKeyboardModalRoute = null;
    }

    private void RenderProfileTarget()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        var targetSessionUser = profile is null ? null : _session.SignedInUsers.FirstOrDefault(user => string.Equals(user.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));
        var sessionStatus = targetSessionUser is null ? "Not currently signed in" : BuildProfileSessionStatus(targetSessionUser);
        var canEdit = profile is not null && actor?.GrevId is not null && AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId);
        _profileView.SetProfile(profile, sessionStatus, canEdit);

        if (profile is not null)
        {
            _ = LoadProfileStatsAsync(profile.GrevId);
        }
    }

    private async Task LoadProfileStatsAsync(string grevId)
    {
        var statsService = _profileStatsService;
        if (statsService is null) return;

        try
        {
            var stats = await statsService.GetAsync(grevId, _runtimeSessions.GetActiveSessions());
            var cloud = await GrevHome.Online.GrevDadAccountDataStore.ReadAsync(_paths,grevId);
            var local = await new GrevHome.Runtime.PlaytimeService(_paths).GetLocalForGrevIdAsync(grevId);
            var own = cloud?.Sources.FirstOrDefault(s=>string.Equals(s.GrevId,grevId,StringComparison.OrdinalIgnoreCase));
            var pending = local.Apps.Values.Sum(a=>a.TotalSeconds) > (own?.TotalSeconds ?? 0) ||
                local.Apps.Values.Sum(a=>a.SessionCount) > (own?.CompletedSessions ?? 0);
            if (_navigation.Current != Route.ProfileView ||
                !string.Equals(GetProfileTarget()?.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _profileView.SetStats(stats);
            _profileView.SetCloudAccountData(cloud,pending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (_navigation.Current == Route.ProfileView &&
                string.Equals(GetProfileTarget()?.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileView.ShowStatsError(ex.Message);
            }
        }
    }

    private void RenderProfileEditor()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        if (profile is null || actor?.GrevId is null) return;
        if (!AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId)) return;
        _profileEditView.SetProfile(profile, AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManageRoles));
    }

    private string BuildProfileSessionStatus(SessionUser user)
    {
        var controllers = _session.GetControllersForUser(user.SessionId);
        var controllerText = controllers.Count == 0 ? "No controller" : string.Join(", ", controllers.Select(index => $"Controller {index + 1}"));
        return $"Signed in  •  {controllerText}{(user.IsPrimary ? "  •  Primary User" : string.Empty)}";
    }

    private LocalProfile? GetProfileTarget()
    {
        var grevId = _profileTargetGrevId ?? _session.PrimaryUser?.GrevId;
        if (string.IsNullOrWhiteSpace(grevId)) return null;
        return _profiles.FirstOrDefault(profile => string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
    }

    private SessionUser? FindSessionUser(Guid sessionUserId) => _session.SignedInUsers.FirstOrDefault(user => user.SessionId == sessionUserId);
}
