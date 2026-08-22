using GrevHome.Navigation;
using GrevHome.Online;

namespace GrevHome;

public partial class MainWindow
{
    private bool _grevDadPrivacySettingsUiIntegrationReady;

    private void InitializeGrevDadPrivacySettingsUiIntegration()
    {
        if (_grevDadPrivacySettingsUiIntegrationReady)
        {
            return;
        }

        _grevDadPrivacySettingsUiIntegrationReady = true;
        _profileView.SaveGrevDadPrivacyRequested += settings => _ = SaveGrevDadPrivacyFromProfileAsync(settings);

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileView)
            {
                _ = RefreshGrevDadPrivacyForProfileAsync();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.ProfileView)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshGrevDadPrivacyForProfileAsync()));
            }
        };
    }

    private async Task RefreshGrevDadPrivacyForProfileAsync()
    {
        var profile = GetProfileTarget();
        if (profile is null)
        {
            _profileView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                "No persistent profile is selected.");
            return;
        }

        try
        {
            var settings = await RequireGrevDadPrivacySettingsService().GetAsync(profile.GrevId);
            if (_navigation.Current == Route.ProfileView &&
                string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileView.SetGrevDadPrivacyState(settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profileView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                $"Sharing is disabled locally until this profile's privacy settings can be read safely: {ex.Message}");
        }
    }

    private async Task SaveGrevDadPrivacyFromProfileAsync(GrevDadPrivacySettings settings)
    {
        var profile = GetProfileTarget();
        if (profile is null || !CanManageGrevDadProfile(profile))
        {
            _profileView.ShowGrevDadStatus("Make this profile the Primary User before changing its Grev.dad privacy settings.");
            return;
        }

        try
        {
            var saved = await RequireGrevDadPrivacySettingsService().SaveAsync(profile.GrevId, settings);
            _profileView.SetGrevDadPrivacyState(saved, "Saved locally for this GrevID profile.");
            await RefreshGrevDadPresenceForAsync(profile.GrevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profileView.ShowGrevDadStatus($"Could not save Grev.dad privacy settings: {ex.Message}");
            await RefreshGrevDadPrivacyForProfileAsync();
        }
    }
}
