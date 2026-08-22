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

        _settingsView.LinkGrevDadRequested += (_, _) => _ = BeginGrevDadLinkFromSettingsAsync();
        _settingsView.CheckGrevDadLinkRequested += (_, _) => _ = PollActiveGrevDadLinkAsync(forceCurrentPrimary: true);
        _settingsView.CancelGrevDadLinkRequested += (_, _) => _ = CancelGrevDadLinkFromSettingsAsync();
        _settingsView.UnlinkGrevDadRequested += (_, _) => _ = UnlinkGrevDadFromSettingsAsync();
        _settingsView.OpenGrevDadApprovalRequested += OpenGrevDadApprovalPage;

        var service = RequireGrevDadAccountService();
        service.SnapshotChanged += (grevId, snapshot) => Dispatcher.BeginInvoke(new Action(() =>
        {
            var profile = GetPrimaryLocalProfile();
            if (profile is null || !string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeGrevDadLinks.TryGetValue(grevId, out var link);
            _settingsView.SetGrevDadState(profile, snapshot, link);
        }));

        _navigation.RouteChanged += route =>
        {
            if (route == Route.Settings)
            {
                _ = RefreshGrevDadSettingsAsync(validateRemote: true);
            }
            else
            {
                StopGrevDadLinkPolling();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.Settings)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshGrevDadSettingsAsync(validateRemote: false)));
            }
        };

        Closed += (_, _) => _grevDadLinkPollTimer.Stop();
    }

    private async Task RefreshGrevDadSettingsAsync(bool validateRemote)
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            _settingsView.SetGrevDadState(null, GrevDadAccountSnapshot.Unlinked);
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

            _activeGrevDadLinks.TryGetValue(profile.GrevId, out var link);
            _settingsView.SetGrevDadState(profile, snapshot, link);

            if (snapshot.State == GrevDadConnectionState.Linking)
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
            _settingsView.SetGrevDadState(profile, cached with
            {
                State = cached.Account is null ? GrevDadConnectionState.Error : GrevDadConnectionState.Offline,
                Message = ex.Message
            });
        }
    }

    private async Task BeginGrevDadLinkFromSettingsAsync()
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            _settingsView.ShowAccountStatus("A local Primary User is required to link Grev.dad.");
            return;
        }

        var service = RequireGrevDadAccountService();
        try
        {
            _settingsView.ShowAccountStatus("Creating a secure Grev.dad link request…");
            var link = await service.BeginLinkAsync(profile, Environment.MachineName);
            _activeGrevDadLinks[profile.GrevId] = link;
            _settingsView.SetGrevDadState(profile, service.GetLastSnapshot(profile.GrevId), link);
            _settingsView.ShowAccountStatus(
                $"Approve code {link.UserCode} on Grev.dad. Grev Home will check automatically while Settings remains open.");
            StartGrevDadLinkPolling(profile.GrevId, link.PollIntervalSeconds);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is ArgumentException)
        {
            _settingsView.ShowAccountStatus($"Grev.dad link could not start: {ex.Message}");
        }
    }

    private async Task PollActiveGrevDadLinkAsync(bool forceCurrentPrimary = false)
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            StopGrevDadLinkPolling();
            return;
        }

        var grevId = forceCurrentPrimary ? profile.GrevId : _grevDadPollingGrevId;
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
            _settingsView.SetGrevDadState(profile, service.GetLastSnapshot(grevId), link);

            switch (result.State)
            {
                case GrevDadLinkPollState.Pending:
                    return;
                case GrevDadLinkPollState.Approved:
                    _activeGrevDadLinks.Remove(grevId);
                    StopGrevDadLinkPolling();
                    _settingsView.SetGrevDadState(profile, service.GetLastSnapshot(grevId));
                    _settingsView.ShowAccountStatus(
                        $"Linked @{result.Account?.Username} to local GrevID {grevId}. The website password was never stored in Grev Home.");
                    await RefreshGrevDadPresenceForAsync(grevId);
                    return;
                case GrevDadLinkPollState.Denied:
                    _settingsView.ShowAccountStatus("The Grev.dad link request was denied.");
                    break;
                case GrevDadLinkPollState.Expired:
                    _settingsView.ShowAccountStatus("The Grev.dad link request expired. Start a new link request when ready.");
                    break;
                case GrevDadLinkPollState.Revoked:
                    _settingsView.ShowAccountStatus("The Grev.dad link request is no longer valid.");
                    break;
            }

            _activeGrevDadLinks.Remove(grevId);
            StopGrevDadLinkPolling();
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            if (forceCurrentPrimary)
            {
                _settingsView.ShowAccountStatus($"Could not check Grev.dad approval: {ex.Message}");
            }
        }
    }

    private async Task CancelGrevDadLinkFromSettingsAsync()
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            return;
        }

        try
        {
            await RequireGrevDadAccountService().CancelPendingLinkAsync(profile.GrevId);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _settingsView.SetGrevDadState(profile, GrevDadAccountSnapshot.Unlinked);
            _settingsView.ShowAccountStatus("Grev.dad link request cancelled locally.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _settingsView.ShowAccountStatus($"Could not cancel the Grev.dad link request: {ex.Message}");
        }
    }

    private async Task UnlinkGrevDadFromSettingsAsync()
    {
        var profile = GetPrimaryLocalProfile();
        if (profile is null)
        {
            return;
        }

        var current = DateTimeOffset.UtcNow;
        if (current > _grevDadUnlinkArmedUntilUtc)
        {
            _grevDadUnlinkArmedUntilUtc = current.AddSeconds(8);
            _settingsView.ShowAccountStatus(
                "Unlink Grev.dad armed. Select Unlink Grev.dad again within 8 seconds to remove this device link. The local GrevID account will remain intact.");
            return;
        }

        _grevDadUnlinkArmedUntilUtc = DateTimeOffset.MinValue;
        try
        {
            await RequireGrevDadAccountService().UnlinkAsync(profile.GrevId, clearLocalIfOffline: true);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _settingsView.SetGrevDadState(profile, GrevDadAccountSnapshot.Unlinked);
            _settingsView.ShowAccountStatus(
                "Grev.dad was unlinked from this GrevID. The local profile, apps, saves and machine role were not changed.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _settingsView.ShowAccountStatus($"Could not unlink Grev.dad: {ex.Message}");
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
            _settingsView.ShowAccountStatus(
                "Opened the Grev.dad approval page in your default browser. Return to Grev Home after approving the link.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _settingsView.ShowAccountStatus(
                $"Windows could not open the approval page. Use the displayed Grev.dad address and approval code instead: {ex.Message}");
        }
    }

    private void StartGrevDadLinkPolling(string grevId, int intervalSeconds)
    {
        _grevDadPollingGrevId = grevId;
        _grevDadLinkPollTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 2, 30));
        if (_navigation.Current == Route.Settings)
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
