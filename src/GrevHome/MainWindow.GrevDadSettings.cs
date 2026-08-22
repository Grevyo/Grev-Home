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

        _profileView.LinkGrevDadRequested += (_, _) => _ = BeginGrevDadLinkFromProfileAsync();
        _profileView.CheckGrevDadLinkRequested += (_, _) => _ = PollActiveGrevDadLinkAsync(forceCurrentTarget: true);
        _profileView.CancelGrevDadLinkRequested += (_, _) => _ = CancelGrevDadLinkFromProfileAsync();
        _profileView.UnlinkGrevDadRequested += (_, _) => _ = UnlinkGrevDadFromProfileAsync();
        _profileView.OpenGrevDadApprovalRequested += OpenGrevDadApprovalPage;

        var service = RequireGrevDadAccountService();
        service.SnapshotChanged += (grevId, snapshot) => Dispatcher.BeginInvoke(new Action(() =>
        {
            var profile = GetProfileTarget();
            if (_navigation.Current != Route.ProfileView ||
                profile is null ||
                !string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _profileView.SetGrevDadContext(profile, CanManageGrevDadProfile(profile));
            _activeGrevDadLinks.TryGetValue(grevId, out var link);
            _profileView.SetGrevDadState(snapshot, link);
        }));

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileView)
            {
                _ = RefreshGrevDadProfileAsync(validateRemote: true);
            }
            else
            {
                StopGrevDadLinkPolling();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.ProfileView)
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
        var profile = GetProfileTarget();
        _profileView.SetGrevDadContext(profile, profile is not null && CanManageGrevDadProfile(profile));
        if (profile is null)
        {
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

            if (_navigation.Current != Route.ProfileView ||
                !string.Equals(GetProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeGrevDadLinks.TryGetValue(profile.GrevId, out var link);
            _profileView.SetGrevDadState(snapshot, link);

            if (snapshot.State == GrevDadConnectionState.Linking && CanManageGrevDadProfile(profile))
            {
                StartGrevDadLinkPolling(profile.GrevId, link?.PollIntervalSeconds ?? 3);
            }
            else
            {
                StopGrevDadLinkPolling();
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            var cached = service.GetLastSnapshot(profile.GrevId);
            _profileView.SetGrevDadState(cached with
            {
                State = cached.Account is null ? GrevDadConnectionState.Error : GrevDadConnectionState.Offline,
                Message = ex.Message
            });
        }
    }

    private async Task BeginGrevDadLinkFromProfileAsync()
    {
        var profile = GetProfileTarget();
        if (profile is null || !CanManageGrevDadProfile(profile))
        {
            _profileView.ShowGrevDadStatus("Make this profile the Primary User before changing its Grev.dad account link.");
            return;
        }

        var service = RequireGrevDadAccountService();
        try
        {
            _profileView.ShowGrevDadStatus("Creating a secure Grev.dad link request…");
            var link = await service.BeginLinkAsync(profile, Environment.MachineName);
            _activeGrevDadLinks[profile.GrevId] = link;
            _profileView.SetGrevDadState(service.GetLastSnapshot(profile.GrevId), link);
            _profileView.ShowGrevDadStatus(
                $"Approve code {link.UserCode} on Grev.dad. Grev Home will check automatically while this profile remains open.");
            StartGrevDadLinkPolling(profile.GrevId, link.PollIntervalSeconds);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is ArgumentException)
        {
            _profileView.ShowGrevDadStatus($"Grev.dad link could not start: {ex.Message}");
        }
    }

    private async Task PollActiveGrevDadLinkAsync(bool forceCurrentTarget = false)
    {
        var profile = GetProfileTarget();
        if (profile is null || !CanManageGrevDadProfile(profile))
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
            _profileView.SetGrevDadState(service.GetLastSnapshot(grevId), link);

            switch (result.State)
            {
                case GrevDadLinkPollState.Pending:
                    return;
                case GrevDadLinkPollState.Approved:
                    _activeGrevDadLinks.Remove(grevId);
                    StopGrevDadLinkPolling();
                    _profileView.SetGrevDadState(service.GetLastSnapshot(grevId));
                    _profileView.ShowGrevDadStatus($"Linked @{result.Account?.Username} to this GrevID profile.");
                    _ = SyncGrevDadProfileSafeAsync(grevId);
                    await RefreshGrevDadPresenceForAsync(grevId);
                    return;
                case GrevDadLinkPollState.Denied:
                    _profileView.ShowGrevDadStatus("The Grev.dad link request was denied.");
                    break;
                case GrevDadLinkPollState.Expired:
                    _profileView.ShowGrevDadStatus("The Grev.dad link request expired. Start a new request when ready.");
                    break;
                case GrevDadLinkPollState.Revoked:
                    _profileView.ShowGrevDadStatus("The Grev.dad link request is no longer valid.");
                    break;
            }

            _activeGrevDadLinks.Remove(grevId);
            StopGrevDadLinkPolling();
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            if (forceCurrentTarget)
            {
                _profileView.ShowGrevDadStatus($"Could not check Grev.dad approval: {ex.Message}");
            }
        }
    }

    private async Task CancelGrevDadLinkFromProfileAsync()
    {
        var profile = GetProfileTarget();
        if (profile is null || !CanManageGrevDadProfile(profile)) return;

        try
        {
            await RequireGrevDadAccountService().CancelPendingLinkAsync(profile.GrevId);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _profileView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            _profileView.ShowGrevDadStatus("Grev.dad link request cancelled locally for this profile.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _profileView.ShowGrevDadStatus($"Could not cancel the Grev.dad link request: {ex.Message}");
        }
    }

    private async Task UnlinkGrevDadFromProfileAsync()
    {
        var profile = GetProfileTarget();
        if (profile is null || !CanManageGrevDadProfile(profile)) return;

        var current = DateTimeOffset.UtcNow;
        if (current > _grevDadUnlinkArmedUntilUtc)
        {
            _grevDadUnlinkArmedUntilUtc = current.AddSeconds(8);
            _profileView.ShowGrevDadStatus(
                "Unlink armed. Select Unlink Grev.dad again within 8 seconds. The local GrevID profile will remain intact.");
            return;
        }

        _grevDadUnlinkArmedUntilUtc = DateTimeOffset.MinValue;
        try
        {
            await RequireGrevDadAccountService().UnlinkAsync(profile.GrevId, clearLocalIfOffline: true);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _profileView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            _profileView.ShowGrevDadStatus("Grev.dad was unlinked from this profile. Local apps, saves, history and role were not changed.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _profileView.ShowGrevDadStatus($"Could not unlink Grev.dad: {ex.Message}");
        }
    }

    private void OpenGrevDadApprovalPage(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            _profileView.ShowGrevDadStatus("Opened the Grev.dad approval page in your default browser.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _profileView.ShowGrevDadStatus($"Windows could not open the approval page: {ex.Message}");
        }
    }

    private void StartGrevDadLinkPolling(string grevId, int intervalSeconds)
    {
        _grevDadPollingGrevId = grevId;
        _grevDadLinkPollTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 2, 30));
        if (_navigation.Current == Route.ProfileView &&
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
