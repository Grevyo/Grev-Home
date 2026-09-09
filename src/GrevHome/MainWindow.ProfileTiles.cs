using System.IO;
using System.Windows;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ProfileTileEditorView _profileTileEditorView = new();
    private ProfileTileService? _profileTileService;
    private ProfileEditRequest? _profileEditDraftBeforeTiles;
    private string? _profileTileMediaGrevId;
    private bool _profileTilesIntegrationReady;

    private void InitializeProfileTilesIntegration()
    {
        if (_profileTilesIntegrationReady) return;
        _profileTilesIntegrationReady = true;
        _profileTileService = new ProfileTileService(_paths);

        _profileEditView.InitializeProfileTilesEditorLink();
        _profileEditView.ProfileTilesRequested += (_, _) => _ = OpenProfileTilesAsync();
        _profileTileEditorView.BackRequested += (_, _) => _navigation.GoBack();
        _profileTileEditorView.SaveRequested += tiles => _ = SaveProfileTilesAsync(tiles);
        _profileTileEditorView.ChooseMediaRequested += (_, _) => OpenProfileTileMediaPicker();
        _profilePhotoPickerView.PhotoSelected += SelectProfileTileMedia;

        // ControllerInputService already has a proper app-input mode. While Profile Tiles is open,
        // D-Pad/A/B and X/Y/View arrive here instead of also going through the shell's generic focus
        // navigator, which avoids the double-input bug the original unwired view would have caused.
        _controllerInput.AppControlPressed += input =>
        {
            if (_navigation.Current != Route.ProfileTiles) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _profileTileEditorView.IsControllerActive = true;
                var action = input.Control switch
                {
                    AppControllerControl.DPadUp => InputAction.Up,
                    AppControllerControl.DPadDown => InputAction.Down,
                    AppControllerControl.DPadLeft => InputAction.Left,
                    AppControllerControl.DPadRight => InputAction.Right,
                    AppControllerControl.A => InputAction.Accept,
                    AppControllerControl.B => InputAction.Back,
                    _ => (InputAction?)null
                };
                if (action.HasValue) HandleProfileTileInput(action.Value, input.ControllerIndex);
                else _profileTileEditorView.HandleExtendedControl(input.Control);
            }));
        };

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileTiles)
            {
                _controllerInput.AppInputMode = true;
                RouteHost.Content = _profileTileEditorView;
                FocusRouteSoon();

                // Returning from the tile media picker via Cancel leaves the selection untouched.
                // Clear the picker-purpose marker so a later normal profile-photo picker has the
                // correct wording again.
                if (_profileTileMediaGrevId is not null)
                {
                    _profileTileMediaGrevId = null;
                    _profilePhotoPickerView.SetPurpose("Choose Profile Photo", "profile photo");
                }
            }
            else
            {
                if (_controllerInput.AppInputMode && route != Route.GrevDadWeb) _controllerInput.AppInputMode = false;
                if (route == Route.ProfileEdit && _profileEditDraftBeforeTiles is not null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_navigation.Current != Route.ProfileEdit || _profileEditDraftBeforeTiles is null) return;
                        _profileEditView.RestoreDraft(_profileEditDraftBeforeTiles);
                        _profileEditDraftBeforeTiles = null;
                    }));
                }
            }

            if (route == Route.ProfileEdit)
            {
                var profile = GetProfileTarget();
                var actor = _session.PrimaryUser;
                var canEdit = profile is not null && actor?.GrevId is not null &&
                    AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId);
                _profileEditView.SetProfileTilesEditorContext(profile, canEdit);
            }
        };

        _session.Changed += (_, _) =>
        {
            var grevId = _session.PrimaryUser?.GrevId;
            if (!string.IsNullOrWhiteSpace(grevId)) _ = _grevDad.SyncProfileTilesNowAsync(grevId);
        };
    }

    private bool HandleProfileTileInput(InputAction action, int? controllerIndex)
    {
        if (_navigation.Current != Route.ProfileTiles) return false;
        _profileTileEditorView.IsControllerActive = controllerIndex.HasValue;
        if (_profileTileEditorView.HandleInput(action)) return true;
        if (action == InputAction.Back)
        {
            _navigation.GoBack();
            return true;
        }
        return true;
    }

    private async Task OpenProfileTilesAsync()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        var service = _profileTileService;
        if (profile is null || actor?.GrevId is null || service is null ||
            !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId)) return;

        _profileEditDraftBeforeTiles = _profileEditView.CaptureDraft();

        // Pull a newer cloud layout before opening. An empty local timestamp loses to a real cloud
        // timestamp, so this is also the fresh-install restore path after relinking Grev Home.
        if (string.Equals(actor.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
        {
            await _grevDad.SyncProfileTilesNowAsync(profile.GrevId);
        }

        try
        {
            var layout = await service.GetAsync(profile.GrevId);
            _profileTileEditorView.Load(layout.Tiles);
            _navigation.Navigate(Route.ProfileTiles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _profileEditView.ShowStatus($"Could not open profile tiles: {ex.Message}");
        }
    }

    private async Task SaveProfileTilesAsync(IReadOnlyList<ProfileTile> tiles)
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        var service = _profileTileService;
        if (profile is null || actor?.GrevId is null || service is null ||
            !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId)) return;

        try
        {
            await service.SaveAsync(profile.GrevId, tiles);
            var synced = string.Equals(actor.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase) &&
                         await _grevDad.SyncProfileTilesNowAsync(profile.GrevId);
            _profileTileEditorView.ShowStatus(synced
                ? "Profile tiles saved and synced with Grev.dad."
                : "Profile tiles saved locally. Grev.dad will sync when the linked account is available.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _profileTileEditorView.ShowStatus(ex.Message);
        }
    }

    private void OpenProfileTileMediaPicker()
    {
        var profile = GetProfileTarget();
        if (profile is null || _navigation.Current != Route.ProfileTiles) return;
        _profileTileMediaGrevId = profile.GrevId;
        _profilePhotoCurrentPath = null;
        _profilePhotoPickerView.SetPurpose("Choose Tile Picture / GIF", "profile tile");
        ShowProfilePhotoHome();
        _navigation.Navigate(Route.ProfilePhotoPicker);
    }

    private async void SelectProfileTileMedia(string path)
    {
        var grevId = _profileTileMediaGrevId;
        var service = _profileTileService;
        if (string.IsNullOrWhiteSpace(grevId) || service is null) return;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new IOException("That image could not be found.");
            if (info.Length > ProfileTileGrid.MaxBackgroundMediaBytes)
                throw new InvalidOperationException("Tile pictures and GIFs must be no more than 1.4 MB.");

            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"))
                throw new InvalidOperationException("Choose a PNG, JPG, JPEG, BMP or GIF image.");

            var mediaRoot = service.GetMediaRoot(grevId);
            Directory.CreateDirectory(mediaRoot);
            var fileName = $"tile-{Guid.NewGuid():N}{extension}";
            var target = Path.Combine(mediaRoot, fileName);
            File.Copy(path, target, overwrite: false);

            if (!_profileTileEditorView.SetActiveMedia(fileName))
            {
                File.Delete(target);
                throw new InvalidOperationException("The selected tile is no longer available.");
            }

            _profileTileMediaGrevId = null;
            _profilePhotoPickerView.SetPurpose("Choose Profile Photo", "profile photo");
            _navigation.GoBack();
            _profileTileEditorView.ShowStatus($"Selected {Path.GetFileName(path)}. Save to keep and sync it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profilePhotoPickerView.ShowError(ex.Message);
        }
    }
}
