using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using GrevHome.Apps;
using GrevHome.Sessions;
using GrevHome.Storage;

namespace GrevHome.Runtime;

public sealed class RuntimeSessionManager : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PersistHeartbeatInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, TrackedLaunchSession> _active = new();
    private readonly ProcessTreeService _processTree;
    private readonly ProcessWindowService _processWindows;
    private readonly PlaytimeService _playtime;
    private readonly AppLaunchResolver _launchResolver;
    private readonly RuntimeStateStore _runtimeStateStore;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _persistGate = new();
    private DateTimeOffset _lastPersistedAtUtc = DateTimeOffset.MinValue;

    public event Action<LaunchSessionSnapshot>? SessionChanged;
    public event Action<LaunchSessionSnapshot>? SessionEnded;

    public RuntimeSessionManager(
        ProcessTreeService processTree,
        ProcessWindowService processWindows,
        PlaytimeService playtime,
        AppLaunchResolver launchResolver,
        RuntimeStateStore? runtimeStateStore = null)
    {
        _processTree = processTree;
        _processWindows = processWindows;
        _playtime = playtime;
        _launchResolver = launchResolver;
        _runtimeStateStore = runtimeStateStore ?? new RuntimeStateStore(new AppPaths());

        RecoverPersistedSessions();
    }

    public IReadOnlyList<LaunchSessionSnapshot> GetActiveSessions() =>
        _active.Values
            .Select(session => session.Snapshot())
            .OrderByDescending(session => session.StartedAtUtc)
            .ToArray();

    public LaunchSessionSnapshot? GetForegroundSession()
    {
        var foregroundProcessId = _processWindows.GetForegroundProcessId();
        if (!foregroundProcessId.HasValue)
        {
            return null;
        }

        foreach (var tracked in _active.Values)
        {
            if (GetValidatedProcessIds(tracked).Contains(foregroundProcessId.Value))
            {
                return tracked.Snapshot();
            }
        }

        return null;
    }

    public bool SwitchTo(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked))
        {
            return false;
        }

        var processIds = GetValidatedProcessIds(tracked);
        return processIds.Count > 0 && _processWindows.TryActivate(processIds);
    }

    public bool RequestClose(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked))
        {
            return false;
        }

        var snapshot = tracked.Snapshot();
        var processIds = GetValidatedProcessIds(tracked);
        if (processIds.Count == 0)
        {
            return false;
        }

        var requested = _processWindows.RequestGracefulClose(processIds, snapshot.StartedAtUtc);
        if (!requested)
        {
            return false;
        }

        tracked.MarkClosing();
        PersistRuntimeState(force: true);
        SessionChanged?.Invoke(tracked.Snapshot());
        return true;
    }

    public bool ForceClose(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked))
        {
            return false;
        }

        var snapshot = tracked.Snapshot();
        var processIds = GetValidatedProcessIds(tracked);
        if (processIds.Count == 0)
        {
            return false;
        }

        var killed = _processWindows.ForceTerminate(processIds, snapshot.StartedAtUtc);
        if (!killed)
        {
            return false;
        }

        tracked.MarkClosing();
        PersistRuntimeState(force: true);
        SessionChanged?.Invoke(tracked.Snapshot());
        return true;
    }

    public Task<LaunchSessionSnapshot> LaunchAsync(
        InstalledAppEntry entry,
        SessionContext sessionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sessionContext);
        cancellationToken.ThrowIfCancellationRequested();

        var primary = sessionContext.PrimaryUser
            ?? throw new InvalidOperationException("Choose a Primary User before launching an app.");

        var participants = sessionContext.SignedInUsers
            .Select(user => new LaunchParticipant(
                user.SessionId,
                user.GrevId,
                user.DisplayName,
                user.AccountKind))
            .ToArray();

        if (participants.Length == 0)
        {
            throw new InvalidOperationException("At least one user must be signed in before launching an app.");
        }

        return LaunchAsync(entry, primary.GrevId, participants, cancellationToken);
    }

    public Task<LaunchSessionSnapshot> LaunchAsync(
        InstalledAppEntry entry,
        string? primaryGrevId,
        IReadOnlyList<LaunchParticipant> participants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(participants);
        cancellationToken.ThrowIfCancellationRequested();

        if (participants.Count == 0)
        {
            throw new InvalidOperationException("At least one participant is required before launching an app.");
        }

        var startInfo = _launchResolver.Resolve(entry, primaryGrevId);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Windows did not start {entry.Manifest.Definition.Name}.");

        var rootIdentity = _processTree.TryGetProcessIdentity(process.Id)
            ?? new RuntimeProcessIdentity(process.Id, process.StartTime.ToUniversalTime());
        var startedAtUtc = DateTimeOffset.UtcNow;
        var tracked = new TrackedLaunchSession(
            entry.Manifest.Definition.AppId,
            entry.Manifest.Definition.Name,
            primaryGrevId,
            participants.ToArray(),
            rootIdentity,
            startedAtUtc);

        if (!_active.TryAdd(tracked.LaunchSessionId, tracked))
        {
            throw new InvalidOperationException("Grev Home could not register the new runtime session.");
        }

        PersistRuntimeState(force: true);
        var snapshot = tracked.Snapshot();
        SessionChanged?.Invoke(snapshot);
        _ = Task.Run(() => MonitorAsync(tracked, _shutdown.Token), CancellationToken.None);
        return Task.FromResult(snapshot);
    }

    private void RecoverPersistedSessions()
    {
        var recoveredAny = false;

        foreach (var record in _runtimeStateStore.Load())
        {
            if (record.LaunchSessionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(record.AppId) ||
                string.IsNullOrWhiteSpace(record.AppName) ||
                record.StartedAtUtc == default ||
                record.Processes.Count == 0 ||
                record.State is LaunchSessionState.Exited or LaunchSessionState.Failed)
            {
                continue;
            }

            var aliveProcesses = _processTree.GetAliveProcessIdentities(record.Processes);
            if (aliveProcesses.Count == 0)
            {
                // The app ended while Grev Home was not running. Do not guess an end time and
                // do not write playtime here: avoiding duplicate/fictional playtime is safer.
                continue;
            }

            var tracked = TrackedLaunchSession.Recover(
                record.LaunchSessionId,
                record.AppId,
                record.AppName,
                record.PrimaryGrevId,
                record.Participants ?? Array.Empty<LaunchParticipant>(),
                record.RootProcessId,
                aliveProcesses,
                record.StartedAtUtc,
                record.LastObservedAliveAtUtc == default ? record.StartedAtUtc : record.LastObservedAliveAtUtc,
                record.State);

            if (!_active.TryAdd(tracked.LaunchSessionId, tracked))
            {
                continue;
            }

            recoveredAny = true;
            _ = Task.Run(() => MonitorAsync(tracked, _shutdown.Token), CancellationToken.None);
        }

        if (recoveredAny || File.Exists(_runtimeStateStore.StateFile))
        {
            PersistRuntimeState(force: true);
        }
    }

    private async Task MonitorAsync(TrackedLaunchSession tracked, CancellationToken cancellationToken)
    {
        DateTimeOffset? noProcessesSince = null;
        var lastKnownCount = tracked.GetKnownProcessIdentities().Count;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var aliveKnown = _processTree.GetAliveProcessIdentities(tracked.GetKnownProcessIdentities());
                var discoveredIds = _processTree.DiscoverDescendants(aliveKnown.Select(process => process.ProcessId));
                var discoveredIdentities = discoveredIds
                    .Select(_processTree.TryGetProcessIdentity)
                    .Where(identity => identity is not null)
                    .Select(identity => identity!)
                    .ToArray();
                tracked.AddProcessIdentities(discoveredIdentities);

                var currentKnown = tracked.GetKnownProcessIdentities();
                if (currentKnown.Count != lastKnownCount)
                {
                    lastKnownCount = currentKnown.Count;
                    PersistRuntimeState(force: true);
                    SessionChanged?.Invoke(tracked.Snapshot());
                }

                var alive = _processTree.GetAliveProcessIdentities(currentKnown);
                if (alive.Count > 0)
                {
                    noProcessesSince = null;
                    tracked.MarkObservedAlive(DateTimeOffset.UtcNow);
                    PersistRuntimeState(force: false);
                }
                else
                {
                    noProcessesSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - noProcessesSince.Value >= ExitGracePeriod)
                    {
                        await FinalizeAsync(tracked, noProcessesSince.Value, failureMessage: null, cancellationToken);
                        return;
                    }
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Grev Home is shutting down. Active state was persisted before cancellation.
        }
        catch (Exception ex)
        {
            await FinalizeAsync(tracked, DateTimeOffset.UtcNow, ex.Message, CancellationToken.None);
        }
    }

    private async Task FinalizeAsync(
        TrackedLaunchSession tracked,
        DateTimeOffset endedAtUtc,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        if (failureMessage is null)
        {
            tracked.MarkExited(endedAtUtc);
        }
        else
        {
            tracked.MarkFailed(failureMessage, endedAtUtc);
        }

        var snapshot = tracked.Snapshot();
        var duration = snapshot.EndedAtUtc!.Value - snapshot.StartedAtUtc;

        await _playtime.RecordSessionAsync(
            snapshot.AppId,
            snapshot.AppName,
            snapshot.Participants,
            duration,
            snapshot.EndedAtUtc.Value,
            cancellationToken);

        _active.TryRemove(snapshot.LaunchSessionId, out _);
        PersistRuntimeState(force: true);
        SessionChanged?.Invoke(snapshot);
        SessionEnded?.Invoke(snapshot);
    }

    private IReadOnlyList<int> GetValidatedProcessIds(TrackedLaunchSession tracked) =>
        _processTree
            .GetAliveProcessIdentities(tracked.GetKnownProcessIdentities())
            .Select(process => process.ProcessId)
            .ToArray();

    private void PersistRuntimeState(bool force)
    {
        lock (_persistGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!force && now - _lastPersistedAtUtc < PersistHeartbeatInterval)
            {
                return;
            }

            var records = _active.Values
                .Select(tracked =>
                {
                    var snapshot = tracked.Snapshot();
                    return new RuntimeSessionRecoveryRecord(
                        snapshot.LaunchSessionId,
                        snapshot.AppId,
                        snapshot.AppName,
                        snapshot.PrimaryGrevId,
                        snapshot.Participants,
                        snapshot.StartedAtUtc,
                        tracked.LastObservedAliveAtUtc,
                        snapshot.State,
                        snapshot.RootProcessId,
                        tracked.GetKnownProcessIdentities());
                })
                .OrderByDescending(record => record.StartedAtUtc)
                .ToArray();

            try
            {
                _runtimeStateStore.Save(records);
                _lastPersistedAtUtc = now;
            }
            catch (IOException)
            {
                // Runtime persistence must never crash or block the shell.
            }
            catch (UnauthorizedAccessException)
            {
                // Runtime continues in-memory even if the recovery file cannot be written.
            }
        }
    }

    public void Dispose()
    {
        PersistRuntimeState(force: true);
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
