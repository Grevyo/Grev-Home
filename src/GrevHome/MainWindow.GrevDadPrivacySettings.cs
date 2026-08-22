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
        _settingsView.SaveGrevDadPrivacyRequested += settings => _ = SaveGrevDadPrivacyFromSettingsAsync(settings);

        _navigation.RouteChanged += route =>
        {
            if (route == Route.Settings)
            {
                _ = RefreshGrevDadPrivacySettingsAsync();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.Settings)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshGrevDadPrivacySettingsAsync()));
            }
        };
    }

    private async Task RefreshGrevDadPrivacySettingsAsync()
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            _settingsView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                "A persistent local Primary User is required to own Grev.dad privacy settings.");
            return;
        }

        try
        {
            var settings = await RequireGrevDadPrivacySettingsService().GetAsync(profile.GrevId);
            _settingsView.SetGrevDadPrivacyState(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _settingsView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                $"Sharing is disabled locally until privacy settings can be read safely: {ex.Message}");
        }
    }

    private async Task SaveGrevDadPrivacyFromSettingsAsync(GrevDadPrivacySettings settings)
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            _settingsView.ShowGrevDadPrivacyStatus("A persistent local Primary User is required to change Grev.dad privacy settings.");
            return;
        }

        try
        {
            var saved = await RequireGrevDadPrivacySettingsService().SaveAsync(profile.GrevId, settings);
            _settingsView.SetGrevDadPrivacyState(saved, "Saved locally for this GrevID.");
            await RefreshGrevDadPresenceForAsync(profile.GrevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _settingsView.ShowGrevDadPrivacyStatus($"Could not save Grev.dad privacy settings: {ex.Message}");
            await RefreshGrevDadPrivacySettingsAsync();
        }
    }
}
