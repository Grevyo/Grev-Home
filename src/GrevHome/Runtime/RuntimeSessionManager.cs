using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using GrevHome.Apps;
using GrevHome.Diagnostics;
using GrevHome.Sessions;
using GrevHome.Storage;

namespace GrevHome.Runtime;

public sealed class RuntimeSessionManager : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PersistHeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RestartGracefulWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestartForceWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ShutdownMonitorWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] CompletionRetryBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    ];

    private readonly ConcurrentDictionary<Guid, TrackedLaunchSession> _active = new();
    private readonly ConcurrentDictionary<Guid, byte> _restarting = new();
    private readonly ConcurrentDictionary<Guid, byte> _finalizing = new();
    private readonly ConcurrentDictionary<Guid, Task> _monitorTasks = new();
    private readonly ConcurrentDictionary<Guid, byte> _completionRetrying = new();
    private readonly ProcessTreeService _processTree;
    private readonly ProcessWindowService _processWindows;
    private readonly PlaytimeService _playtime;
    private readonly SessionHistoryService _sessionHistory;
    private readonly AppLaunchResolver _launchResolver;
    private readonly RuntimeStateStore _runtimeStateStore;
    private readonly RuntimeCompletionStore _completionStore;
    private readonly RuntimeRecoveryJournal _recoveryJournal;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _persistGate = new();
    private DateTimeOffset _lastPersistedAtUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public event Action<LaunchSessionSnapshot>? SessionChanged;
    public event Action<LaunchSessionSnapshot>? SessionEnded;
    public event Action<LaunchSessionSnapshot>? SessionHistoryCommitted;
    public event Action<LaunchSessionSnapshot, string>? SessionCompletionDeferred;
    public event Action<LaunchSessionSnapshot>? SessionCompletionRecovered;

    public RuntimeSessionManager(
        ProcessTreeService processTree,
        ProcessWindowService processWindows,
        PlaytimeService playtime,
        AppLaunchResolver launchResolver,
        RuntimeStateStore? runtimeStateStore = null,
        SessionHistoryService? sessionHistory = null,
        RuntimeCompletionStore? completionStore = null)
    {
        _processTree = processTree;
        _processWindows = processWindows;
        _playtime = playtime;
        _launchResolver = launchResolver;

        var fallbackPaths = new AppPaths();
        _runtimeStateStore = runtimeStateStore ?? new RuntimeStateStore(fallbackPaths);
        _sessionHistory = sessionHistory ?? new SessionHistoryService(fallbackPaths);
        _completionStore = completionStore ?? new RuntimeCompletionStore(fallbackPaths);
        _recoveryJournal = new RuntimeRecoveryJournal(fallbackPaths);

        // Startup recovery performs async durability work and historically blocked on it synchronously.
        // Running the recovery boundary on a worker context prevents those awaits from trying to resume
        // on the WPF dispatcher while MainWindow construction is still blocking that dispatcher.
        Task.Run(() =>
        {
            ReplayPendingCompletions();
            RecoverPersistedSessions();
        }).GetAwaiter().GetResult();
    }

    public IReadOnlyList<LaunchSessionSnapshot> GetActiveSessions() =>
        _active.Values
            .Select(session => session.Snapshot())
            .OrderByDescending(session => session.StartedAtUtc)
            .ToArray();

    public void NotifySystemSuspend(DateTimeOffset suspendedAtUtc)
    {
        if (_disposed) return;
        var sessions = _active.Values.ToArray();
        foreach (var tracked in sessions) tracked.MarkSuspended(suspendedAtUtc);
        if (sessions.Length == 0) return;
        PersistRuntimeState(force: true);
        foreach (var tracked in sessions) SessionChanged?.Invoke(tracked.Snapshot());
    }

    public void NotifySystemResume(DateTimeOffset resumedAtUtc)
    {
        if (_disposed) return;
        var sessions = _active.Values.ToArray();
        foreach (var tracked in sessions) tracked.MarkResumed(resumedAtUtc);
        if (sessions.Length == 0) return;
        PersistRuntimeState(force: true);
        foreach (var tracked in sessions) SessionChanged?.Invoke(tracked.Snapshot());
    }

    public LaunchSessionSnapshot? GetSession(Guid launchSessionId) =>
        _active.TryGetValue(launchSessionId, out var tracked) ? tracked.Snapshot() : null;

    public LaunchSessionSnapshot? GetForegroundSession()
    {
        var foregroundProcessId = _processWindows.GetForegroundProcessId();
        if (!foregroundProcessId.HasValue) return null;

        foreach (var tracked in _active.Values)
        {
            RefreshDeclaredProcesses(tracked);
            if (GetValidatedProcessIds(tracked).Contains(foregroundProcessId.Value)) return tracked.Snapshot();
        }
        return null;
    }

    public bool SwitchTo(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked)) return false;
        RefreshDeclaredProcesses(tracked);
        var processIds = GetValidatedProcessIds(tracked);
        return processIds.Count > 0 && _processWindows.TryActivate(processIds);
    }

    public bool RequestClose(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked)) return false;
        var processIds = GetValidatedProcessIds(tracked);
        if (processIds.Count == 0) return false;
        var requested = _processWindows.RequestGracefulClose(GetValidatedProcessIdentities(tracked));
        if (!requested) return false;
        tracked.MarkClosing();
        PersistRuntimeState(force: true);
        SessionChanged?.Invoke(tracked.Snapshot());
        return true;
    }

    public bool ForceClose(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked)) return false;
        var processIds = GetValidatedProcessIds(tracked);
        if (processIds.Count == 0) return false;
        var killed = _processWindows.ForceTerminate(
            GetValidatedProcessIdentities(tracked),
            tracked.ForceKillEntireProcessTree);
        if (!killed) return false;
        tracked.MarkClosing();
        PersistRuntimeState(force: true);
        SessionChanged?.Invoke(tracked.Snapshot());
        return true;
    }

    public async Task<LaunchSessionSnapshot> RestartAsync(
        Guid launchSessionId,
        InstalledAppEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_restarting.TryAdd(launchSessionId, 0))
            throw new InvalidOperationException("That app is already restarting.");

        try
        {
            if (!_active.TryGetValue(launchSessionId, out var tracked))
                throw new InvalidOperationException("That runtime session is no longer active.");

            var snapshot = tracked.Snapshot();
            if (!string.Equals(entry.Manifest.Definition.AppId, snapshot.AppId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The installed app no longer matches the running session.");
            if (!entry.AvailableToCurrentUser)
                throw new InvalidOperationException(entry.AvailabilityMessage ?? "That app is not currently available to this profile.");

            if (GetValidatedProcessIds(tracked).Count > 0)
            {
                if (!RequestClose(launchSessionId)) ForceClose(launchSessionId);
                await WaitForProcessesToExitAsync(tracked, RestartGracefulWait, cancellationToken);
                if (GetValidatedProcessIds(tracked).Count > 0)
                {
                    ForceClose(launchSessionId);
                    await WaitForProcessesToExitAsync(tracked, RestartForceWait, cancellationToken);
                }
                if (GetValidatedProcessIds(tracked).Count > 0)
                    throw new InvalidOperationException($"Windows did not close {snapshot.AppName}, so Grev Home did not start a duplicate copy.");
            }

            var finalizeDeadline = DateTimeOffset.UtcNow.Add(ExitGracePeriod + TimeSpan.FromSeconds(3));
            while (_active.ContainsKey(launchSessionId) && DateTimeOffset.UtcNow < finalizeDeadline)
                await Task.Delay(100, cancellationToken);

            if (_active.ContainsKey(launchSessionId))
                throw new InvalidOperationException("The previous runtime session has not finished its local completion commit, so Grev Home did not start a replacement yet.");

            return await LaunchAsync(entry, snapshot.PrimaryGrevId, snapshot.Participants, cancellationToken);
        }
        finally
        {
            _restarting.TryRemove(launchSessionId, out _);
        }
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
            .Select(user => new LaunchParticipant(user.SessionId, user.GrevId, user.DisplayName, user.AccountKind))
            .ToArray();
        if (participants.Length == 0)
            throw new InvalidOperationException("At least one user must be signed in before launching an app.");
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (participants.Count == 0)
            throw new InvalidOperationException("At least one participant is required before launching an app.");

        if (ShouldReuseRunningSession(entry))
        {
            var existing = FindReusableSession(entry.Manifest.Definition.AppId);
            if (existing is not null)
            {
                RefreshDeclaredProcesses(existing);
                DeduplicateActiveSessions();
                PersistRuntimeState(force: true);
                var reusedSnapshot = existing.Snapshot();
                SessionChanged?.Invoke(reusedSnapshot);
                return Task.FromResult(reusedSnapshot);
            }
        }

        var startInfo = _launchResolver.Resolve(entry, primaryGrevId);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Windows did not start {entry.Manifest.Definition.Name}.");
        var rootIdentity = _processTree.TryGetProcessIdentity(process.Id)
            ?? new RuntimeProcessIdentity(process.Id, process.StartTime.ToUniversalTime());
        var startedAtUtc = DateTimeOffset.UtcNow;
        var launch = entry.Manifest.Definition.Launch;
        var systemInstalled = entry.Manifest.Definition.InstallStrategy == InstallStrategy.SystemInstalled;
        var tracked = new TrackedLaunchSession(
            entry.Manifest.Definition.AppId,
            entry.Manifest.Definition.Name,
            primaryGrevId,
            systemInstalled ? launch.ProcessName : null,
            systemInstalled ? launch.AdditionalProcessNames : null,
            launch.EffectiveTrackDescendantProcesses,
            launch.EffectiveForceKillEntireProcessTree,
            participants.ToArray(),
            rootIdentity,
            startedAtUtc);
        RefreshDeclaredProcesses(tracked);
        if (!_active.TryAdd(tracked.LaunchSessionId, tracked))
            throw new InvalidOperationException("Grev Home could not register the new runtime session.");

        if (ShouldReuseRunningSession(entry))
        {
            DeduplicateActiveSessions();
            if (!_active.ContainsKey(tracked.LaunchSessionId))
            {
                var canonical = FindReusableSession(entry.Manifest.Definition.AppId);
                if (canonical is not null)
                {
                    PersistRuntimeState(force: true);
                    return Task.FromResult(canonical.Snapshot());
                }
            }
        }

        PersistRuntimeState(force: true);
        var snapshot = tracked.Snapshot();
        SessionChanged?.Invoke(snapshot);
        StartMonitor(tracked);
        return Task.FromResult(snapshot);
    }

    private void ReplayPendingCompletions()
    {
        foreach (var record in _completionStore.LoadAll())
        {
            try
            {
                CommitPendingCompletionAsync(record, publishHistoryEvent: false).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (IsCompletionPersistenceFailure(ex))
            {
                StartCompletionRetry(record);
            }
        }
    }

    private void RecoverPersistedSessions()
    {
        var recoveredAny = false;
        var safeToRewriteRecoveryState = true;
        foreach (var record in _runtimeStateStore.Load())
        {
            if (record.LaunchSessionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(record.AppId) ||
                string.IsNullOrWhiteSpace(record.AppName) ||
                record.StartedAtUtc == default ||
                record.Processes is null || record.Processes.Count == 0 ||
                record.State is LaunchSessionState.Exited or LaunchSessionState.Failed)
                continue;

            var aliveProcesses = _processTree.GetAliveProcessIdentities(record.Processes);
            if (aliveProcesses.Count == 0)
            {
                foreach (var processName in GetDeclaredProcessNames(record.ProcessName, record.AdditionalProcessNames))
                {
                    aliveProcesses = aliveProcesses
                        .Concat(_processTree.GetProcessIdentitiesByName(processName))
                        .GroupBy(identity => identity.ProcessId)
                        .Select(group => group.First())
                        .ToArray();
                }
            }

            if (aliveProcesses.Count == 0)
            {
                safeToRewriteRecoveryState &= TryRecoverDeadPersistedSession(record);
                continue;
            }

            var tracked = TrackedLaunchSession.Recover(
                record.LaunchSessionId,
                record.AppId,
                record.AppName,
                record.PrimaryGrevId,
                record.ProcessName,
                record.AdditionalProcessNames,
                record.TrackDescendantProcesses ?? true,
                record.ForceKillEntireProcessTree ?? true,
                record.Participants ?? Array.Empty<LaunchParticipant>(),
                record.RootProcessId,
                aliveProcesses,
                record.StartedAtUtc,
                record.LastObservedAliveAtUtc == default ? record.StartedAtUtc : record.LastObservedAliveAtUtc,
                record.State,
                record.AccumulatedSuspendedSeconds,
                record.SuspendedAtUtc);
            if (record.SuspendedAtUtc is not null) tracked.MarkResumed(DateTimeOffset.UtcNow);
            if (!_active.TryAdd(tracked.LaunchSessionId, tracked)) continue;
            recoveredAny = true;
        }

        DeduplicateActiveSessions();
        foreach (var tracked in _active.Values) StartMonitor(tracked);
        if (safeToRewriteRecoveryState && (recoveredAny || File.Exists(_runtimeStateStore.StateFile)))
        {
            PersistRuntimeState(force: true);
        }
    }

    private bool TryRecoverDeadPersistedSession(RuntimeSessionRecoveryRecord record)
    {
        var endedAtUtc = record.LastObservedAliveAtUtc == default
            ? record.StartedAtUtc
            : record.LastObservedAliveAtUtc < record.StartedAtUtc
                ? record.StartedAtUtc
                : record.LastObservedAliveAtUtc;
        var suspendedSeconds = Math.Max(0L, record.AccumulatedSuspendedSeconds);
        if (record.SuspendedAtUtc is DateTimeOffset suspendedAtUtc && suspendedAtUtc < endedAtUtc)
        {
            suspendedSeconds = checked(suspendedSeconds + Math.Max(
                0L,
                (long)Math.Floor((endedAtUtc - suspendedAtUtc).TotalSeconds)));
        }

        var wallClockSeconds = Math.Max(
            0L,
            (long)Math.Floor((endedAtUtc - record.StartedAtUtc).TotalSeconds));
        var trackedSeconds = Math.Max(0L, wallClockSeconds - suspendedSeconds);
        var snapshot = new LaunchSessionSnapshot(
            record.LaunchSessionId,
            record.AppId,
            record.AppName,
            record.PrimaryGrevId,
            record.Participants ?? Array.Empty<LaunchParticipant>(),
            record.StartedAtUtc,
            endedAtUtc,
            LaunchSessionState.Exited,
            record.RootProcessId,
            (record.Processes ?? Array.Empty<RuntimeProcessIdentity>())
                .Select(process => process.ProcessId)
                .Where(processId => processId > 0)
                .Distinct()
                .ToArray(),
            null,
            trackedSeconds);

        try
        {
            var pending = _completionStore
                .SaveAsync(snapshot, endedAtUtc, null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _recoveryJournal.TryAppend(
                "startup-dead-session-handoff",
                pending.ToSnapshot(),
                "Persisted runtime processes were no longer alive. Completion duration was conservatively capped at the last confirmed-alive timestamp.");

            try
            {
                CommitPendingCompletionAsync(pending, publishHistoryEvent: false)
                    .GetAwaiter()
                    .GetResult();
                _recoveryJournal.TryAppend(
                    "startup-dead-session-committed",
                    pending.ToSnapshot(),
                    "Conservative dead-session recovery committed local playtime and immutable session history.");
            }
            catch (Exception ex) when (IsCompletionPersistenceFailure(ex))
            {
                _recoveryJournal.TryAppend(
                    "startup-dead-session-deferred",
                    pending.ToSnapshot(),
                    $"Dead-session recovery is durable but local completion commit was deferred: {ex.Message}");
                StartCompletionRetry(pending);
            }

            return true;
        }
        catch (Exception ex) when (IsCompletionPersistenceFailure(ex))
        {
            _recoveryJournal.TryAppend(
                "startup-dead-session-handoff-failed",
                snapshot,
                $"Could not create a durable completion handoff; original runtime recovery state was retained: {ex.Message}");
            return false;
        }
    }

    private void StartMonitor(TrackedLaunchSession tracked)
    {
        var id = tracked.LaunchSessionId;
        var task = Task.Run(() => MonitorAsync(tracked, _shutdown.Token), CancellationToken.None);
        _monitorTasks[id] = task;
        _ = task.ContinueWith(
            completedTask => _monitorTasks.TryRemove(id, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task MonitorAsync(TrackedLaunchSession tracked, CancellationToken cancellationToken)
    {
        DateTimeOffset? noProcessesSince = null;
        var lastKnownCount = tracked.GetKnownProcessIdentities().Count;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_active.ContainsKey(tracked.LaunchSessionId)) return;
                RefreshDeclaredProcesses(tracked);
                if (tracked.TrackDescendantProcesses)
                {
                    var aliveKnown = _processTree.GetAliveProcessIdentities(tracked.GetKnownProcessIdentities());
                    var discoveredIds = _processTree.DiscoverDescendants(aliveKnown.Select(process => process.ProcessId));
                    tracked.AddProcessIdentities(discoveredIds
                        .Select(_processTree.TryGetProcessIdentity)
                        .Where(identity => identity is not null)
                        .Select(identity => identity!));
                }

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
                        await FinalizeAsync(tracked, noProcessesSince.Value, null);
                        return;
                    }
                }
                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await FinalizeAsync(tracked, DateTimeOffset.UtcNow, ex.Message);
        }
    }

    private async Task FinalizeAsync(TrackedLaunchSession tracked, DateTimeOffset endedAtUtc, string? failureMessage)
    {
        if (!_active.ContainsKey(tracked.LaunchSessionId) || !_finalizing.TryAdd(tracked.LaunchSessionId, 0)) return;
        try
        {
            var completionSnapshot = tracked.Snapshot() with
            {
                TrackedDurationSeconds = tracked.GetTrackedDurationSeconds(endedAtUtc)
            };
            var pending = await _completionStore.SaveAsync(completionSnapshot, endedAtUtc, failureMessage, CancellationToken.None);
            try
            {
                await CommitPendingCompletionAsync(pending, publishHistoryEvent: true);
            }
            catch (Exception ex) when (IsCompletionPersistenceFailure(ex))
            {
                SessionCompletionDeferred?.Invoke(pending.ToSnapshot(), ex.Message);
                StartCompletionRetry(pending);
            }

            if (failureMessage is null) tracked.MarkExited(endedAtUtc);
            else tracked.MarkFailed(failureMessage, endedAtUtc);
            var snapshot = tracked.Snapshot();
            _active.TryRemove(snapshot.LaunchSessionId, out _);
            PersistRuntimeState(force: true);
            SessionChanged?.Invoke(snapshot);
            SessionEnded?.Invoke(snapshot);
        }
        finally
        {
            _finalizing.TryRemove(tracked.LaunchSessionId, out _);
        }
    }

    private void StartCompletionRetry(RuntimePendingCompletionRecord pending)
    {
        if (!_completionRetrying.TryAdd(pending.LaunchSessionId, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; !_shutdown.IsCancellationRequested; attempt++)
                {
                    var delay = CompletionRetryBackoff[Math.Min(attempt, CompletionRetryBackoff.Length - 1)];
                    await Task.Delay(delay, _shutdown.Token);
                    try
                    {
                        await CommitPendingCompletionAsync(pending, publishHistoryEvent: true);
                        SessionCompletionRecovered?.Invoke(pending.ToSnapshot());
                        return;
                    }
                    catch (Exception ex) when (IsCompletionPersistenceFailure(ex))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
            finally
            {
                _completionRetrying.TryRemove(pending.LaunchSessionId, out _);
            }
        }, CancellationToken.None);
    }

    private async Task CommitPendingCompletionAsync(RuntimePendingCompletionRecord pending, bool publishHistoryEvent)
    {
        var snapshot = pending.ToSnapshot();
        var durationSeconds = snapshot.TrackedDurationSeconds ??
                              Math.Max(0L, (long)Math.Round((snapshot.EndedAtUtc!.Value - snapshot.StartedAtUtc).TotalSeconds));
        await _playtime.RecordSessionAsync(
            snapshot.LaunchSessionId,
            snapshot.AppId,
            snapshot.AppName,
            snapshot.Participants,
            TimeSpan.FromSeconds(Math.Max(0L, durationSeconds)),
            snapshot.EndedAtUtc!.Value,
            CancellationToken.None);
        await _sessionHistory.RecordAsync(snapshot, CancellationToken.None);
        _completionStore.Delete(snapshot.LaunchSessionId);
        if (publishHistoryEvent) SessionHistoryCommitted?.Invoke(snapshot);
    }

    private static bool IsCompletionPersistenceFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or OverflowException;

    private async Task WaitForProcessesToExitAsync(
        TrackedLaunchSession tracked,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetValidatedProcessIds(tracked).Count == 0) return;
            await Task.Delay(150, cancellationToken);
        }
    }

    private TrackedLaunchSession? FindReusableSession(string appId)
    {
        foreach (var tracked in _active.Values
                     .Where(session => string.Equals(session.AppId, appId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(session => session.StartedAtUtc))
        {
            RefreshDeclaredProcesses(tracked);
            var snapshot = tracked.Snapshot();
            if (snapshot.State != LaunchSessionState.Closing && GetValidatedProcessIds(tracked).Count > 0) return tracked;
        }
        return null;
    }

    private static bool ShouldReuseRunningSession(InstalledAppEntry entry)
    {
        var definition = entry.Manifest.Definition;
        return definition.Launch.SingleInstance ||
               (definition.InstallStrategy == InstallStrategy.SystemInstalled && definition.Launch.DeclaredProcessNames.Count > 0);
    }

    private void RefreshDeclaredProcesses(TrackedLaunchSession tracked)
    {
        foreach (var processName in tracked.DeclaredProcessNames)
            tracked.AddProcessIdentities(_processTree.GetProcessIdentitiesByName(processName));
    }

    private static IReadOnlyList<string> GetDeclaredProcessNames(string? primary, IReadOnlyList<string>? additional) =>
        new[] { primary }
            .Concat(additional ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void DeduplicateActiveSessions()
    {
        var claimedProcesses = new HashSet<(string AppId, int ProcessId)>();
        var changed = false;
        foreach (var tracked in _active.Values.OrderBy(session => session.StartedAtUtc).ToArray())
        {
            RefreshDeclaredProcesses(tracked);
            var alive = GetValidatedProcessIdentities(tracked);
            if (alive.Count == 0) continue;
            var duplicate = alive.Any(process => claimedProcesses.Contains((tracked.AppId.ToUpperInvariant(), process.ProcessId)));
            if (duplicate)
            {
                changed |= _active.TryRemove(tracked.LaunchSessionId, out _);
                continue;
            }
            foreach (var process in alive) claimedProcesses.Add((tracked.AppId.ToUpperInvariant(), process.ProcessId));
        }
        if (changed) PersistRuntimeState(force: true);
    }

    private IReadOnlyList<RuntimeProcessIdentity> GetValidatedProcessIdentities(TrackedLaunchSession tracked) =>
        _processTree.GetAliveProcessIdentities(tracked.GetKnownProcessIdentities());

    private IReadOnlyList<int> GetValidatedProcessIds(TrackedLaunchSession tracked) =>
        GetValidatedProcessIdentities(tracked).Select(process => process.ProcessId).ToArray();

    private void PersistRuntimeState(bool force)
    {
        lock (_persistGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!force && now - _lastPersistedAtUtc < PersistHeartbeatInterval) return;
            var records = _active.Values.Select(tracked =>
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
                    tracked.GetKnownProcessIdentities(),
                    tracked.ProcessName,
                    tracked.AdditionalProcessNames,
                    tracked.TrackDescendantProcesses,
                    tracked.ForceKillEntireProcessTree,
                    tracked.AccumulatedSuspendedSeconds,
                    tracked.SuspendedAtUtc);
            }).OrderByDescending(record => record.StartedAtUtc).ToArray();
            try
            {
                _runtimeStateStore.Save(records);
                _lastPersistedAtUtc = now;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void FinalizeDeadSessionsBeforeShutdown()
    {
        foreach (var tracked in _active.Values.ToArray())
        {
            try
            {
                RefreshDeclaredProcesses(tracked);
                if (GetValidatedProcessIds(tracked).Count > 0)
                {
                    continue;
                }

                var endedAtUtc = tracked.LastObservedAliveAtUtc < tracked.StartedAtUtc
                    ? tracked.StartedAtUtc
                    : tracked.LastObservedAliveAtUtc;
                FinalizeAsync(tracked, endedAtUtc, null).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (IsCompletionPersistenceFailure(ex))
            {
                // Keep the tracked session in sessions.json. The next launch can conservatively
                // hand it to the crash-safe completion envelope rather than losing it.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Before cancelling monitor tasks, synchronously finish any session whose processes are
        // already gone. This closes the clean-shutdown race where the app exited just before the
        // shell and the monitor had not yet reached its two-second exit grace boundary.
        PersistRuntimeState(force: true);
        FinalizeDeadSessionsBeforeShutdown();

        _disposed = true;
        PersistRuntimeState(force: true);
        _shutdown.Cancel();
        var monitors = _monitorTasks.Values.ToArray();
        if (monitors.Length > 0)
        {
            try { Task.WaitAll(monitors, ShutdownMonitorWait); }
            catch (AggregateException) { }
        }
        PersistRuntimeState(force: true);
        _shutdown.Dispose();
    }
}
