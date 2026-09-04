using System.Diagnostics;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome;

public partial class MainWindow
{
    private readonly Dictionary<string, GrevDadLinkStart> _activeGrevDadLinks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _grevDadLinkPollTimer = new();
    private bool _grevDadSettingsIntegrationReady;
    private string? _grevDadPollingGrevId;
    private DateTimeOffset _grevDadUnlinkArmedUntilUtc;

    private void InitializeGrevDadSettingsIntegration()
    {
        if (_grevDadSettingsIntegrationReady)
        {
            return;
        }

        _grevDadSettingsIntegrationReady = true;
        _grevDadLinkPollTimer.Interval = TimeSpan.FromSeconds(3);
        _grevDadLinkPollTimer.Tick += (_, _) => _ = PollActiveGrevDadLinkAsync();

        _profileEditView.LinkGrevDadRequested += (_, _) => _ = BeginGrevDadLinkFromProfileAsync();
        _profileEditView.CheckGrevDadLinkRequested += (_, _) => _ = PollActiveGrevDadLinkAsync(forceCurrentTarget: true);
        _profileEditView.CancelGrevDadLinkRequested += (_, _) => _ = CancelGrevDadLinkFromProfileAsync();
        _profileEditView.UnlinkGrevDadRequested += (_, _) => _ = UnlinkGrevDadFromProfileAsync();
        _profileEditView.OpenGrevDadApprovalRequested += OpenGrevDadApprovalPage;
        _profileEditView.OpenGrevDadWebsiteRequested += (_,_)=>OpenGrevDadWebsite(RequireGrevDadAccountService().BaseUri);

        var service = RequireGrevDadAccountService();
        service.SnapshotChanged += (grevId, snapshot) => Dispatcher.BeginInvoke(new Action(() =>
        {
            var profile = GetProfileTarget();
            if (profile is null || !string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_navigation.Current == Route.ProfileEdit)
            {
                _profileEditView.SetGrevDadContext(profile, CanManageGrevDadProfile(profile));
                _activeGrevDadLinks.TryGetValue(grevId, out var link);
                _profileEditView.SetGrevDadState(snapshot, link);
            }
            else if (_navigation.Current == Route.ProfileView)
            {
                _profileView.SetGrevDadState(snapshot);
            }
        }));

        _navigation.RouteChanged += route =>
        {
            if (route is Route.ProfileEdit or Route.ProfileView)
            {
                _ = RefreshGrevDadProfileAsync(validateRemote: route == Route.ProfileEdit);
            }
            else
            {
                StopGrevDadLinkPolling();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current is Route.ProfileEdit or Route.ProfileView)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshGrevDadProfileAsync(validateRemote: false)));
            }
        };

        Closed += (_, _) => _grevDadLinkPollTimer.Stop();
    }

    private bool CanManageGrevDadProfile(LocalProfile profile) =>
        !string.IsNullOrWhiteSpace(_session.PrimaryUser?.GrevId) &&
        string.Equals(_session.PrimaryUser!.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshGrevDadProfileAsync(bool validateRemote)
    {
        var route = _navigation.Current;
        var profile = GetProfileTarget();
        if (route == Route.ProfileEdit)
        {
            _profileEditView.SetGrevDadContext(profile, profile is not null && CanManageGrevDadProfile(profile));
        }

        if (profile is null)
        {
            if (route == Route.ProfileView)
            {
                _profileView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            }
            StopGrevDadLinkPolling();
            return;
        }

        var service = RequireGrevDadAccountService();
        try
        {
            var snapshot = await service.LoadLocalStateAsync(profile.GrevId);
            if (validateRemote && snapshot.State == GrevDadConnectionState.Linked)
            {
                snapshot = await service.ValidateLinkedAccountAsync(profile.GrevId);
            }

            if (_navigation.Current != route ||
                !string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (route == Route.ProfileEdit)
            {
                _activeGrevDadLinks.TryGetValue(profile.GrevId, out var link);
                _profileEditView.SetGrevDadState(snapshot, link);

                if (snapshot.State == GrevDadConnectionState.Linking && CanManageGrevDadProfile(profile))
                {
                    StartGrevDadLinkPolling(profile.GrevId, link?.PollIntervalSeconds ?? 3);
                }
                else
                {
                    StopGrevDadLinkPolling();
                }
            }
            else if (route == Route.ProfileView)
            {
                _profileView.SetGrevDadState(snapshot);
                StopGrevDadLinkPolling();
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            var cached = service.GetLastSnapshot(profile.GrevId) with
            {
                State = service.GetLastSnapshot(profile.GrevId).Account is null
                    ? GrevDadConnectionState.Error
                    : GrevDadConnectionState.Offline,
                Message = ex.Message
            };

            if (route == Route.ProfileEdit)
            {
                _profileEditView.SetGrevDadState(cached);
            }
            else if (route == Route.ProfileView)
            {
                _profileView.SetGrevDadState(cached);
            }
        }
    }

    private async Task BeginGrevDadLinkFromProfileAsync()
    {
        var profile = GetProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile))
        {
            _profileEditView.ShowGrevDadStatus("This profile must be the current Primary User before its Grev.dad account link can be changed.");
            return;
        }

        var service = RequireGrevDadAccountService();
        var maintenance = _grevDadMaintenance;
        if (maintenance is null)
        {
            _profileEditView.ShowGrevDadStatus("Grev.dad integration is still initializing. Try Link Grev.dad again.");
            return;
        }

        try
        {
            _profileEditView.ShowGrevDadStatus("Checking the live Grev.dad integration contract…");
            var capabilities = await maintenance.GetCapabilitiesAsync(forceRefresh: true);
            if (!capabilities.Capabilities.Linking || !capabilities.Capabilities.DeviceTokens)
            {
                _profileEditView.ShowGrevDadStatus(
                    $"Grev.dad {capabilities.Environment} API {capabilities.ApiVersion} is online but does not advertise the required linking and device-token capabilities.");
                return;
            }

            _profileEditView.ShowGrevDadStatus(
                $"Grev.dad {capabilities.Environment} API {capabilities.ApiVersion} is ready. Creating a secure link request…");
            var link = await service.BeginLinkAsync(profile, Environment.MachineName);
            _activeGrevDadLinks[profile.GrevId] = link;
            _profileEditView.SetGrevDadState(service.GetLastSnapshot(profile.GrevId), link);
            _profileEditView.ShowGrevDadStatus(
                $"Approve code {link.UserCode} on Grev.dad. Grev Home will check automatically while Edit Profile remains open.");
            StartGrevDadLinkPolling(profile.GrevId, link.PollIntervalSeconds);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is ArgumentException)
        {
            _profileEditView.ShowGrevDadStatus(
                $"Grev.dad linking is not ready on {service.BaseUri.Host}: {ex.Message}");
        }
    }

    private async Task PollActiveGrevDadLinkAsync(bool forceCurrentTarget = false)
    {
        var profile = GetProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile))
        {
            StopGrevDadLinkPolling();
            return;
        }

        var grevId = forceCurrentTarget ? profile.GrevId : _grevDadPollingGrevId;
        if (string.IsNullOrWhiteSpace(grevId) ||
            !string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
        {
            StopGrevDadLinkPolling();
            return;
        }

        var service = RequireGrevDadAccountService();
        try
        {
            var result = await service.PollLinkAsync(grevId);
            _activeGrevDadLinks.TryGetValue(grevId, out var link);
            _profileEditView.SetGrevDadState(service.GetLastSnapshot(grevId), link);

            switch (result.State)
            {
                case GrevDadLinkPollState.Pending:
                    return;
                case GrevDadLinkPollState.Approved:
                    _activeGrevDadLinks.Remove(grevId);
                    StopGrevDadLinkPolling();
                    var approved = service.GetLastSnapshot(grevId);
                    _profileEditView.SetGrevDadState(approved);
                    _profileEditView.ShowGrevDadStatus($"Linked @{result.Account?.Username} to this GrevID profile.");
                    _ = SyncGrevDadProfileSafeAsync(grevId);
                    await RefreshGrevDadPresenceForAsync(grevId);
                    return;
                case GrevDadLinkPollState.Denied:
                    _profileEditView.ShowGrevDadStatus("The Grev.dad link request was denied.");
                    break;
                case GrevDadLinkPollState.Expired:
                    _profileEditView.ShowGrevDadStatus("The Grev.dad link request expired. Start a new request when ready.");
                    break;
                case GrevDadLinkPollState.Revoked:
                    _profileEditView.ShowGrevDadStatus("The Grev.dad link request is no longer valid.");
                    break;
            }

            _activeGrevDadLinks.Remove(grevId);
            StopGrevDadLinkPolling();
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            if (forceCurrentTarget)
            {
                _profileEditView.ShowGrevDadStatus($"Could not check Grev.dad approval: {ex.Message}");
            }
        }
    }

    private async Task CancelGrevDadLinkFromProfileAsync()
    {
        var profile = GetProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile)) return;

        try
        {
            await RequireGrevDadAccountService().CancelPendingLinkAsync(profile.GrevId);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _profileEditView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            _profileEditView.ShowGrevDadStatus("Grev.dad link request cancelled locally for this profile.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _profileEditView.ShowGrevDadStatus($"Could not cancel the Grev.dad link request: {ex.Message}");
        }
    }

    private async Task UnlinkGrevDadFromProfileAsync()
    {
        var profile = GetProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile)) return;

        var current = DateTimeOffset.UtcNow;
        if (current > _grevDadUnlinkArmedUntilUtc)
        {
            _grevDadUnlinkArmedUntilUtc = current.AddSeconds(8);
            _profileEditView.ShowGrevDadStatus(
                "Unlink armed. Select Unlink Grev.dad again within 8 seconds. The local GrevID profile will remain intact.");
            return;
        }

        _grevDadUnlinkArmedUntilUtc = DateTimeOffset.MinValue;
        try
        {
            await RequireGrevDadAccountService().UnlinkAsync(profile.GrevId, clearLocalIfOffline: true);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _profileEditView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            _profileEditView.ShowGrevDadStatus("Grev.dad was unlinked from this profile. Local apps, saves, history and role were not changed.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _profileEditView.ShowGrevDadStatus($"Could not unlink Grev.dad: {ex.Message}");
        }
    }

    private void OpenGrevDadApprovalPage(Uri uri)
    {
        OpenGrevDadWebsite(uri);
    }

    private void StartGrevDadLinkPolling(string grevId, int intervalSeconds)
    {
        _grevDadPollingGrevId = grevId;
        _grevDadLinkPollTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 2, 30));
        if (_navigation.Current == Route.ProfileEdit &&
            string.Equals(GetProfileTarget()?.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
        {
            _grevDadLinkPollTimer.Start();
        }
    }

    private void StopGrevDadLinkPolling()
    {
        _grevDadPollingGrevId = null;
        _grevDadLinkPollTimer.Stop();
    }
}
