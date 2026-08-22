using System.Collections.Concurrent;
using System.Windows.Threading;
using GrevHome.Online;
using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ConcurrentDictionary<Guid, byte> _grevDadPublishedRuntimeSessions = new();
    private readonly DispatcherTimer _grevDadHeartbeatTimer = new()
    {
        Interval = TimeSpan.FromMinutes(2)
    };

    private GrevDadAccountService? _grevDadAccounts;
    private bool _grevDadIntegrationReady;
    private string? _lastPrimaryGrevDadGrevId;

    private void InitializeGrevDadIntegration()
    {
        if (_grevDadIntegrationReady)
        {
            return;
        }

        _grevDadIntegrationReady = true;
        _grevDadAccounts = new GrevDadAccountService(_paths);

        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = HandlePrimaryGrevDadChangedAsync()));
        _runtimeSessions.SessionChanged += HandleGrevDadRuntimeChanged;
        _runtimeSessions.SessionEnded += HandleGrevDadRuntimeEnded;
        _grevDadHeartbeatTimer.Tick += (_, _) => _ = RefreshGrevDadPresenceHeartbeatAsync();
        Closed += (_, _) =>
        {
            _grevDadHeartbeatTimer.Stop();
            _grevDadAccounts?.Dispose();
        };

        _ = HandlePrimaryGrevDadChangedAsync();
    }

    private GrevDadAccountService RequireGrevDadAccountService() =>
        _grevDadAccounts
        ?? throw new InvalidOperationException("Grev.dad account services have not been initialized.");

    private async Task HandlePrimaryGrevDadChangedAsync()
    {
        var service = _grevDadAccounts;
        if (service is null)
        {
            return;
        }

        var primaryGrevId = _session.PrimaryUser?.GrevId;
        var previous = _lastPrimaryGrevDadGrevId;
        _lastPrimaryGrevDadGrevId = primaryGrevId;

        if (!string.IsNullOrWhiteSpace(previous) &&
            !string.Equals(previous, primaryGrevId, StringComparison.OrdinalIgnoreCase))
        {
            _ = SetGrevDadPresenceSafeAsync(previous, "offline", "none", "", expiresInSeconds: 60);
        }

        if (string.IsNullOrWhiteSpace(primaryGrevId))
        {
            if (_runtimeSessions.GetActiveSessions().All(session => string.IsNullOrWhiteSpace(session.PrimaryGrevId)))
            {
                _grevDadHeartbeatTimer.Stop();
            }
            return;
        }

        try
        {
            var local = await service.LoadLocalStateAsync(primaryGrevId);
            if (local.State == GrevDadConnectionState.Linked)
            {
                var validated = await service.ValidateLinkedAccountAsync(primaryGrevId);
                if (validated.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
                {
                    _grevDadHeartbeatTimer.Start();
                    await RefreshGrevDadPresenceForAsync(primaryGrevId);
                }
            }
            else if (local.State == GrevDadConnectionState.Offline)
            {
                _grevDadHeartbeatTimer.Start();
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // Online identity is additive. Local Grev Home login/navigation must not fail because
            // the site, network or Windows credential store is unavailable.
        }
    }

    private void HandleGrevDadRuntimeChanged(LaunchSessionSnapshot snapshot)
    {
        if (snapshot.State != LaunchSessionState.Running || string.IsNullOrWhiteSpace(snapshot.PrimaryGrevId))
        {
            return;
        }

        if (_grevDadPublishedRuntimeSessions.TryAdd(snapshot.LaunchSessionId, 0))
        {
            _ = PublishGrevDadRuntimeActivitySafeAsync(snapshot, started: true);
        }
    }

    private void HandleGrevDadRuntimeEnded(LaunchSessionSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.PrimaryGrevId))
        {
            return;
        }

        if (_grevDadPublishedRuntimeSessions.TryRemove(snapshot.LaunchSessionId, out _))
        {
            _ = PublishGrevDadRuntimeActivitySafeAsync(snapshot, started: false);
        }
        else
        {
            _ = RefreshGrevDadPresenceForAsync(snapshot.PrimaryGrevId);
        }
    }

    private async Task PublishGrevDadRuntimeActivitySafeAsync(LaunchSessionSnapshot snapshot, bool started)
    {
        var service = _grevDadAccounts;
        var grevId = snapshot.PrimaryGrevId;
        if (service is null || string.IsNullOrWhiteSpace(grevId))
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

            await service.PublishAppActivityAsync(
                grevId,
                started,
                snapshot.AppId,
                snapshot.AppName,
                detail: started ? "Started from Grev Home" : "Stopped in Grev Home");
            await RefreshGrevDadPresenceForAsync(grevId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // Presence/activity delivery must never affect app launch, runtime tracking or playtime.
        }
    }

    private async Task RefreshGrevDadPresenceHeartbeatAsync()
    {
        var grevIds = _runtimeSessions.GetActiveSessions()
            .Select(session => session.PrimaryGrevId)
            .Append(_session.PrimaryUser?.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
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
        if (service is null)
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

            var active = _runtimeSessions.GetActiveSessions()
                .Where(session => string.Equals(session.PrimaryGrevId, grevId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(session => session.StartedAtUtc)
                .FirstOrDefault();

            if (active is not null)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "online",
                    "playing",
                    active.AppName,
                    expiresInSeconds: 300);
            }
            else if (string.Equals(_session.PrimaryUser?.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
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

    private static bool IsExpectedGrevDadBackgroundFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException or InvalidOperationException or
            InvalidDataException or UnauthorizedAccessException or Win32Exception;
}
