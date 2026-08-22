using System.Collections.Concurrent;
using System.Windows.Threading;
using GrevHome.Online;
using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ConcurrentDictionary<string, byte> _grevDadPublishedRuntimeSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _grevDadHeartbeatTimer = new()
    {
        Interval = TimeSpan.FromMinutes(2)
    };
    private HashSet<string> _lastSignedInGrevDadGrevIds = new(StringComparer.OrdinalIgnoreCase);

    private GrevDadAccountService? _grevDadAccounts;
    private GrevDadPrivacySettingsService? _grevDadPrivacy;
    private bool _grevDadIntegrationReady;

    private void InitializeGrevDadIntegration()
    {
        if (_grevDadIntegrationReady)
        {
            return;
        }

        _grevDadIntegrationReady = true;
        _grevDadAccounts = new GrevDadAccountService(_paths);
        _grevDadPrivacy = new GrevDadPrivacySettingsService(_paths);

        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = HandleGrevDadSessionChangedAsync()));
        _runtimeSessions.SessionChanged += HandleGrevDadRuntimeChanged;
        _runtimeSessions.SessionEnded += HandleGrevDadRuntimeEnded;
        _grevDadHeartbeatTimer.Tick += (_, _) => _ = RefreshGrevDadPresenceHeartbeatAsync();
        Closed += (_, _) =>
        {
            _grevDadHeartbeatTimer.Stop();
            _grevDadAccounts?.Dispose();
        };

        _ = HandleGrevDadSessionChangedAsync();
    }

    private GrevDadAccountService RequireGrevDadAccountService() =>
        _grevDadAccounts
        ?? throw new InvalidOperationException("Grev.dad account services have not been initialized.");

    private GrevDadPrivacySettingsService RequireGrevDadPrivacySettingsService() =>
        _grevDadPrivacy
        ?? throw new InvalidOperationException("Grev.dad privacy services have not been initialized.");

    private async Task HandleGrevDadSessionChangedAsync()
    {
        var service = _grevDadAccounts;
        if (service is null)
        {
            return;
        }

        var current = _session.SignedInUsers
            .Select(user => user.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = _lastSignedInGrevDadGrevIds
            .Where(grevId => !current.Contains(grevId))
            .ToArray();
        _lastSignedInGrevDadGrevIds = current;

        foreach (var grevId in removed)
        {
            _ = SetGrevDadPresenceSafeAsync(grevId, "offline", "none", "", expiresInSeconds: 60);
        }

        var activeParticipantGrevIds = GetAllActiveRuntimeGrevIds();
        if (current.Count == 0 && activeParticipantGrevIds.Count == 0)
        {
            _grevDadHeartbeatTimer.Stop();
            return;
        }

        foreach (var grevId in current.Concat(activeParticipantGrevIds).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var local = await service.LoadLocalStateAsync(grevId);
                if (local.State == GrevDadConnectionState.Linked)
                {
                    var validated = await service.ValidateLinkedAccountAsync(grevId);
                    if (validated.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
                    {
                        _grevDadHeartbeatTimer.Start();
                        await RefreshGrevDadPresenceForAsync(grevId);
                    }
                }
                else if (local.State == GrevDadConnectionState.Offline)
                {
                    _grevDadHeartbeatTimer.Start();
                }
            }
            catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
            {
                // Grev.dad is additive. One participant's online-account failure must never affect
                // any other local GrevID, controller assignment or runtime session.
            }
        }
    }

    private void HandleGrevDadRuntimeChanged(LaunchSessionSnapshot snapshot)
    {
        if (snapshot.State != LaunchSessionState.Running)
        {
            return;
        }

        foreach (var grevId in GetPersistentParticipantGrevIds(snapshot))
        {
            var key = BuildRuntimePublicationKey(snapshot.LaunchSessionId, grevId);
            if (_grevDadPublishedRuntimeSessions.TryAdd(key, 0))
            {
                _ = PublishGrevDadRuntimeActivitySafeAsync(snapshot, grevId, started: true);
            }
        }
    }

    private void HandleGrevDadRuntimeEnded(LaunchSessionSnapshot snapshot)
    {
        foreach (var grevId in GetPersistentParticipantGrevIds(snapshot))
        {
            var key = BuildRuntimePublicationKey(snapshot.LaunchSessionId, grevId);
            if (_grevDadPublishedRuntimeSessions.TryRemove(key, out _))
            {
                _ = PublishGrevDadRuntimeActivitySafeAsync(snapshot, grevId, started: false);
            }
            else
            {
                _ = RefreshGrevDadPresenceForAsync(grevId);
            }
        }
    }

    private async Task PublishGrevDadRuntimeActivitySafeAsync(
        LaunchSessionSnapshot snapshot,
        string grevId,
        bool started)
    {
        var service = _grevDadAccounts;
        var privacyService = _grevDadPrivacy;
        if (service is null || privacyService is null || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        try
        {
            var local = service.GetLastSnapshot(grevId);
            if (local.State == GrevDadConnectionState.Unlinked)
            {
                local = await service.LoadLocalStateAsync(grevId);
            }

            if (local.State is not (GrevDadConnectionState.Linked or GrevDadConnectionState.Offline))
            {
                return;
            }

            var privacy = await privacyService.GetAsync(grevId);
            if (privacy.ShareLiveActivityEvents)
            {
                await service.PublishAppActivityAsync(
                    grevId,
                    started,
                    snapshot.AppId,
                    snapshot.AppName,
                    detail: started ? "Started from Grev Home" : "Stopped in Grev Home",
                    visibility: privacy.ActivityVisibility);
            }

            await RefreshGrevDadPresenceForAsync(grevId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // Presence/activity delivery must never affect app launch, runtime tracking or playtime.
        }
    }

    private async Task RefreshGrevDadPresenceHeartbeatAsync()
    {
        var grevIds = GetAllActiveRuntimeGrevIds()
            .Concat(_session.SignedInUsers
                .Select(user => user.GrevId)
                .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
                .Select(grevId => grevId!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (grevIds.Length == 0)
        {
            _grevDadHeartbeatTimer.Stop();
            return;
        }

        foreach (var grevId in grevIds)
        {
            await RefreshGrevDadPresenceForAsync(grevId);
        }
    }

    private async Task RefreshGrevDadPresenceForAsync(string grevId)
    {
        var service = _grevDadAccounts;
        var privacyService = _grevDadPrivacy;
        if (service is null || privacyService is null)
        {
            return;
        }

        try
        {
            var local = service.GetLastSnapshot(grevId);
            if (local.State == GrevDadConnectionState.Unlinked)
            {
                local = await service.LoadLocalStateAsync(grevId);
            }

            if (local.State is not (GrevDadConnectionState.Linked or GrevDadConnectionState.Offline))
            {
                return;
            }

            var privacy = await privacyService.GetAsync(grevId);
            if (!privacy.SharePresence)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "offline",
                    "none",
                    "",
                    expiresInSeconds: 60);
                return;
            }

            var active = _runtimeSessions.GetActiveSessions()
                .Where(session => GetPersistentParticipantGrevIds(session).Contains(grevId, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(session => session.StartedAtUtc)
                .FirstOrDefault();
            var signedIn = _session.SignedInUsers.Any(user =>
                string.Equals(user.GrevId, grevId, StringComparison.OrdinalIgnoreCase));

            if (active is not null && privacy.SharePlayingStatus)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "online",
                    "playing",
                    active.AppName,
                    expiresInSeconds: 300);
            }
            else if (active is not null || signedIn)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "online",
                    "none",
                    "",
                    expiresInSeconds: 300);
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // Heartbeats are best effort and expire server-side. Never block the local appliance.
        }
    }

    private async Task SetGrevDadPresenceSafeAsync(
        string grevId,
        string availability,
        string activityType,
        string activityText,
        int expiresInSeconds)
    {
        var service = _grevDadAccounts;
        if (service is null)
        {
            return;
        }

        try
        {
            await service.UpdatePresenceAsync(
                grevId,
                availability,
                activityType,
                activityText,
                expiresInSeconds: expiresInSeconds);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
        }
    }

    private IReadOnlyList<string> GetAllActiveRuntimeGrevIds() =>
        _runtimeSessions.GetActiveSessions()
            .SelectMany(GetPersistentParticipantGrevIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> GetPersistentParticipantGrevIds(LaunchSessionSnapshot snapshot) =>
        snapshot.Participants
            .Select(participant => participant.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildRuntimePublicationKey(Guid sessionId, string grevId) =>
        $"{sessionId:N}:{grevId}";

    private static bool IsExpectedGrevDadBackgroundFailure(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or TimeoutException or InvalidOperationException or
            InvalidDataException or UnauthorizedAccessException or Win32Exception;
}
