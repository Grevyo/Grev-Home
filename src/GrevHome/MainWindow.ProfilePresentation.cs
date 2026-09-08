using System.IO;
using System.Security.Cryptography;
using System.Windows;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ProfilePhotoPickerView _profileBannerPickerView = new();
    private ProfilePresentationSettingsService? _profilePresentationService;
    private string? _profileBannerCurrentPath;
    private bool _profileBannerPickerActive;
    private bool _profilePresentationReady;

    private void InitializeProfilePresentationIntegration()
    {
        if (_profilePresentationReady)
        {
            return;
        }

        _profilePresentationReady = true;
        _profilePresentationService = new ProfilePresentationSettingsService(_paths);

        _profileEditView.ChooseCustomBannerRequested += (_, _) => OpenProfileBannerPicker();
        _profileEditView.SaveRequested += request => _ = SaveProfilePresentationAsync(request);

        _profileBannerPickerView.HomeRequested += (_, _) => ShowProfileBannerHome();
        _profileBannerPickerView.UpRequested += (_, _) => NavigateProfileBannerUp();
        _profileBannerPickerView.CancelRequested += (_, _) => _navigation.GoBack();
        _profileBannerPickerView.NavigateRequested += NavigateProfileBannerPath;
        _profileBannerPickerView.PhotoSelected += SelectProfileBanner;
        _profileBannerPickerView.SetPurpose("Choose Profile Banner", "profile banner");

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfilePhotoPicker && _profileBannerPickerActive)
            {
                RouteHost.Content = _profileBannerPickerView;
                FocusRouteSoon();
                return;
            }

            if (route is Route.ProfileView or Route.ProfileEdit)
            {
                _ = RefreshProfilePresentationForCurrentRouteAsync(route);
            }

            if (route != Route.ProfilePhotoPicker)
            {
                _profileBannerPickerActive = false;
            }
        };

        _session.Changed += (_, _) =>
        {
            if (_navigation.Current is Route.ProfileView or Route.ProfileEdit)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshProfilePresentationForCurrentRouteAsync(_navigation.Current)));
            }
        };
    }

    private async Task RefreshProfilePresentationForCurrentRouteAsync(Route route)
    {
        var service = _profilePresentationService;
        var profile = GetProfileTarget();
        if (service is null || profile is null)
        {
            return;
        }

        try
        {
            var presentation = await service.GetAsync(profile.GrevId);
            if (_navigation.Current != route ||
                !string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (route == Route.ProfileView)
            {
                _profileView.SetPresentation(presentation);
            }
            else if (route == Route.ProfileEdit)
            {
                _profileEditView.ApplyPresentationSettings(presentation);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (route == Route.ProfileEdit)
            {
                _profileEditView.ShowStatus($"Profile presentation could not be loaded safely: {ex.Message}");
            }
        }
    }

    private async Task SaveProfilePresentationAsync(ProfileEditRequest request)
    {
        var service = _profilePresentationService;
        var actor = _session.PrimaryUser;
        var profile = _profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.GrevId, request.GrevId, StringComparison.OrdinalIgnoreCase));
        if (service is null || actor?.GrevId is null || profile is null || profile.IsBuiltInGuest ||
            !AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId))
        {
            return;
        }

        var localAppearanceSaved = false;
        try
        {
            var saved = await service.SaveAsync(
                profile.GrevId,
                request.BannerKey,
                request.ShowcaseMode,
                request.CustomBannerSourcePath,
                request.CardFrame,
                request.AvatarShape,
                request.ShowUsername,
                request.ShowLevel,
                request.ShowXp,
                request.ShowPlaytime,
                request.ShowSessions,
                request.ShowStatus);
            localAppearanceSaved = true;

            // Profile details/avatar and presentation are stored in separate files. The edit view
            // raises one Save action for both, so wait for the authoritative profile write before
            // publishing the combined public card. This prevents an avatar/bio/status edit from
            // racing the Grev.dad upload and briefly restoring the previous public values.
            var profileForCard = await AwaitCompletedProfileSaveAsync(request, profile);
            await _grevDad.SavePublicCardIfLinkedAsync(profile.GrevId, new Online.GrevDadPublicCard(
                saved.BannerKey,
                saved.CardFrame.ToString().ToLowerInvariant(),
                saved.AvatarShape.ToString().ToLowerInvariant(),
                saved.ShowUsername,
                saved.ShowLevel,
                saved.ShowXp,
                saved.ShowPlaytime,
                saved.ShowSessions,
                saved.ShowStatus,
                ProfileMediaDataUrl.TryRead(_paths, profile.GrevId, profileForCard.AvatarImageFile),
                ProfileMediaDataUrl.TryRead(_paths, profile.GrevId, saved.BannerImageFile),
                profileForCard.Bio,
                profileForCard.StatusMessage));

            if (_navigation.Current == Route.ProfileEdit &&
                string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileEditView.ApplyPresentationSettings(saved);
            }
            else if (_navigation.Current == Route.ProfileView &&
                     string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileView.SetPresentation(saved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            if (_navigation.Current == Route.ProfileEdit &&
                string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileEditView.ShowStatus(localAppearanceSaved
                    ? $"Profile appearance was saved locally, but the combined public card could not be synced yet: {ex.Message}"
                    : $"Profile appearance could not be saved: {ex.Message}");
            }
        }
    }

    private async Task<LocalProfile> AwaitCompletedProfileSaveAsync(
        ProfileEditRequest request,
        LocalProfile previous)
    {
        var expectedAvatarHash = string.IsNullOrWhiteSpace(request.CustomAvatarSourcePath)
            ? null
            : ComputeFileHash(request.CustomAvatarSourcePath);

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var current = (await _profileService.GetProfilesAsync()).FirstOrDefault(candidate =>
                string.Equals(candidate.GrevId, request.GrevId, StringComparison.OrdinalIgnoreCase));
            if (current is not null && ProfileEditMatches(request, current, expectedAvatarHash))
            {
                return current;
            }
            await Task.Delay(20);
        }

        // Never publish known-stale combined data. Local presentation is already safe; the next
        // normal Grev.dad refresh/save will retry the public card after the profile write succeeds.
        throw new InvalidOperationException(
            $"The profile detail save for {previous.DisplayName} did not complete cleanly, so stale public profile data was not uploaded.");
    }

    private bool ProfileEditMatches(ProfileEditRequest request, LocalProfile current, byte[]? expectedAvatarHash)
    {
        if (!string.Equals(current.DisplayName, request.DisplayName.Trim(), StringComparison.Ordinal) ||
            !string.Equals(current.Bio, request.Bio.Trim(), StringComparison.Ordinal) ||
            !string.Equals(current.StatusMessage, request.StatusMessage.Trim(), StringComparison.Ordinal) ||
            !string.Equals(current.AvatarKey, request.AvatarKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedAvatarHash is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(current.AvatarImageFile))
        {
            return false;
        }

        try
        {
            var avatarPath = Path.Combine(_paths.GetProfileRoot(current.GrevId), current.AvatarImageFile);
            if (!File.Exists(avatarPath)) return false;
            var savedHash = ComputeFileHash(avatarPath);
            return CryptographicOperations.FixedTimeEquals(expectedAvatarHash, savedHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(Path.GetFullPath(path));
        return SHA256.HashData(stream);
    }

    private void OpenProfileBannerPicker()
    {
        var draft = _profileEditView.CaptureDraft();
        if (draft is null)
        {
            return;
        }

        _profileEditDraftBeforePhotoPicker = draft;
        _profileBannerCurrentPath = null;
        _profileBannerPickerActive = true;
        ShowProfileBannerHome();
        _navigation.Navigate(Route.ProfilePhotoPicker);
    }

    private void ShowProfileBannerHome()
    {
        _profileBannerCurrentPath = null;
        try
        {
            var locations = _fileSystem.GetHomeLocations(_paths.Root)
                .Where(location => location.Name is not "Test Area" and not "Grev Home Data")
                .ToArray();
            _profileBannerPickerView.ShowHome(locations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _profileBannerPickerView.ShowError(ex.Message);
        }
    }

    private void NavigateProfileBannerPath(string path)
    {
        try
        {
            var entries = _fileSystem.GetEntries(path);
            _profileBannerCurrentPath = path;
            _profileBannerPickerView.ShowDirectory(path, entries, _fileSystem.GetParent(path) is not null);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _profileBannerPickerView.ShowError(ex.Message);
        }
    }

    private void NavigateProfileBannerUp()
    {
        if (string.IsNullOrWhiteSpace(_profileBannerCurrentPath))
        {
            ShowProfileBannerHome();
            return;
        }

        var parent = _fileSystem.GetParent(_profileBannerCurrentPath);
        if (parent is null) ShowProfileBannerHome();
        else NavigateProfileBannerPath(parent);
    }

    private void SelectProfileBanner(string path)
    {
        if (_profileEditDraftBeforePhotoPicker is null)
        {
            return;
        }

        _profileEditDraftBeforePhotoPicker = _profileEditDraftBeforePhotoPicker with
        {
            BannerKey = ProfileBannerCatalog.CustomKey,
            CustomBannerSourcePath = path
        };
        _profileBannerPickerActive = false;
        _navigation.GoBack();
        _profileEditView.ShowStatus($"Selected banner {Path.GetFileName(path)}. Save Profile to copy it into this GrevID.");
    }
}
