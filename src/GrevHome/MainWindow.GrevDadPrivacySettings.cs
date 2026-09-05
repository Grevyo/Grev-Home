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
        _profileEditView.SaveGrevDadPrivacyRequested += settings => _ = SaveGrevDadPrivacyFromProfileAsync(settings);

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileEdit)
            {
                _ = RefreshGrevDadPrivacyForProfileAsync();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.ProfileEdit)
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
            _profileEditView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                "No persistent profile is selected.");
            return;
        }

        try
        {
            var settings = await RequireGrevDadPrivacySettingsService().GetAsync(profile.GrevId);
            if (_navigation.Current == Route.ProfileEdit &&
                string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileEditView.SetGrevDadPrivacyState(settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profileEditView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                $"Sharing is disabled locally until this profile's privacy settings can be read safely: {ex.Message}");
        }
    }

    private async Task SaveGrevDadPrivacyFromProfileAsync(GrevDadPrivacySettings settings)
    {
        var profile = GetProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile))
        {
            _profileEditView.ShowGrevDadStatus("This profile must be the current Primary User before its Grev.dad privacy settings can be changed.");
            return;
        }

        try
        {
            var saved = await RequireGrevDadPrivacySettingsService().SaveAsync(profile.GrevId, settings);
            _profileEditView.SetGrevDadPrivacyState(saved, "Saved locally for this GrevID profile.");
            await RefreshGrevDadPresenceForAsync(profile.GrevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profileEditView.ShowGrevDadStatus($"Could not save Grev.dad privacy settings: {ex.Message}");
            await RefreshGrevDadPrivacyForProfileAsync();
        }
    }
}
