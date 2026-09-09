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
        _controllerInput.AppControlPressed += input =>
        {
            if (_navigation.Current != Route.ProfileTiles) return;
            Dispatcher.BeginInvoke(new Action(() => _profileTileEditorView.HandleExtendedControl(input.Control)));
        };

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileTiles)
            {
                _controllerInput.AppInputMode = true;
                RouteHost.Content = _profileTileEditorView;
                FocusRouteSoon();
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

    /// <summary>Called by MainWindow's central input router before generic focus navigation.</summary>
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
        return true; // this route owns shell navigation; never let D-Pad also move button focus.
    }

    private async Task OpenProfileTilesAsync()
    {
        var profile = GetProfileTarget();
        var actor = _session.PrimaryUser;
        var service = _profileTileService;
        if (profile is null || actor?.GrevId is null || service is null ||
            !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId)) return;

        _profileEditDraftBeforeTiles = _profileEditView.CaptureDraft();

        // For the user's own linked profile, pull any newer grev.dad layout first. This is also the
        // fresh-install restore path because an empty local timestamp loses to cloud updatedAt.
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
            _navigation.GoBack();
            _profileTileEditorView.ShowStatus($"Selected {Path.GetFileName(path)}. Save to keep and sync it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profilePhotoPickerView.ShowError(ex.Message);
        }
    }
}
