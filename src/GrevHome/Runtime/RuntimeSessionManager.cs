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
    private static readonly TimeSpan RestartGracefulWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestartForceWait = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<Guid, TrackedLaunchSession> _active = new();
    private readonly ConcurrentDictionary<Guid, byte> _restarting = new();
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

    public LaunchSessionSnapshot? GetSession(Guid launchSessionId) =>
        _active.TryGetValue(launchSessionId, out var tracked)
            ? tracked.Snapshot()
            : null;

    public LaunchSessionSnapshot? GetForegroundSession()
    {
        var foregroundProcessId = _processWindows.GetForegroundProcessId();
        if (!foregroundProcessId.HasValue)
        {
            return null;
        }

        foreach (var tracked in _active.Values)
        {
            RefreshDeclaredProcesses(tracked);
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

        // A tray-style app can replace or hide its original top-level window while keeping the
        // same named process group alive. Refresh those identities before attempting activation.
        RefreshDeclaredProcesses(tracked);
        var processIds = GetValidatedProcessIds(tracked);
        return processIds.Count > 0 && _processWindows.TryActivate(processIds);
    }

    public bool RequestClose(Guid launchSessionId)
    {
        if (!_active.TryGetValue(launchSessionId, out var tracked))
        {
            return false;
        }

        var processIds = GetValidatedProcessIds(tracked);
        if (processIds.Count == 0)
        {
            return false;
        }

        var requested = _processWindows.RequestGracefulClose(
            GetValidatedProcessIdentities(tracked));
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

        var processIds = GetValidatedProcessIds(tracked);
        if (processIds.Count == 0)
        {
            return false;
        }

        var killed = _processWindows.ForceTerminate(
            GetValidatedProcessIdentities(tracked),
            tracked.ForceKillEntireProcessTree);
        if (!killed)
        {
            return false;
        }

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
        {
            throw new InvalidOperationException("That app is already restarting.");
        }

        try
        {
            if (!_active.TryGetValue(launchSessionId, out var tracked))
            {
                throw new InvalidOperationException("That runtime session is no longer active.");
            }

            var snapshot = tracked.Snapshot();
            if (!string.Equals(
                    entry.Manifest.Definition.AppId,
                    snapshot.AppId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The installed app no longer matches the running session.");
            }

            if (!entry.AvailableToCurrentUser)
            {
                throw new InvalidOperationException(
                    entry.AvailabilityMessage ?? "That app is not currently available to this profile.");
            }

            var processIds = GetValidatedProcessIds(tracked);
            if (processIds.Count > 0)
            {
                if (!RequestClose(launchSessionId))
                {
                    ForceClose(launchSessionId);
                }

                await WaitForProcessesToExitAsync(tracked, RestartGracefulWait, cancellationToken);

                if (GetValidatedProcessIds(tracked).Count > 0)
                {
                    ForceClose(launchSessionId);
                    await WaitForProcessesToExitAsync(tracked, RestartForceWait, cancellationToken);
                }

                if (GetValidatedProcessIds(tracked).Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Windows did not close {snapshot.AppName}, so Grev Home did not start a duplicate copy.");
                }
            }

            // Give the normal monitor a brief chance to finalize the old playtime record before
            // registering the replacement session. A replacement may still start if finalization
            // is finishing in parallel because it receives its own LaunchSessionId.
            var finalizeDeadline = DateTimeOffset.UtcNow.Add(ExitGracePeriod + TimeSpan.FromSeconds(1));
            while (_active.ContainsKey(launchSessionId) && DateTimeOffset.UtcNow < finalizeDeadline)
            {
                await Task.Delay(100, cancellationToken);
            }

            return await LaunchAsync(
                entry,
                snapshot.PrimaryGrevId,
                snapshot.Participants,
                cancellationToken);
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
        var declaredProcessName = systemInstalled ? launch.ProcessName : null;
        var additionalProcessNames = systemInstalled ? launch.AdditionalProcessNames : null;
        var tracked = new TrackedLaunchSession(
            entry.Manifest.Definition.AppId,
            entry.Manifest.Definition.Name,
            primaryGrevId,
            declaredProcessName,
            additionalProcessNames,
            launch.EffectiveTrackDescendantProcesses,
            launch.EffectiveForceKillEntireProcessTree,
            participants.ToArray(),
            rootIdentity,
            startedAtUtc);

        RefreshDeclaredProcesses(tracked);

        if (!_active.TryAdd(tracked.LaunchSessionId, tracked))
        {
            throw new InvalidOperationException("Grev Home could not register the new runtime session.");
        }

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
                record.Processes is null ||
                record.Processes.Count == 0 ||
                record.State is LaunchSessionState.Exited or LaunchSessionState.Failed)
            {
                continue;
            }

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
                // The app ended while Grev Home was not running. Do not guess an end time and
                // do not write playtime here: avoiding duplicate/fictional playtime is safer.
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
                record.State);

            if (!_active.TryAdd(tracked.LaunchSessionId, tracked))
            {
                continue;
            }

            recoveredAny = true;
        }

        // Older Discord builds could persist multiple Grev launch records that all adopted the
        // same app process group. Keep the oldest canonical session and discard any later records
        // that overlap the same live Windows processes before monitors/playtime start.
        DeduplicateActiveSessions();

        foreach (var tracked in _active.Values)
        {
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
                // A session removed by deduplication must stop silently. It must not later write a
                // second playtime record for the canonical app process group.
                if (!_active.ContainsKey(tracked.LaunchSessionId))
                {
                    return;
                }

                RefreshDeclaredProcesses(tracked);

                if (tracked.TrackDescendantProcesses)
                {
                    var aliveKnown = _processTree.GetAliveProcessIdentities(tracked.GetKnownProcessIdentities());
                    var discoveredIds = _processTree.DiscoverDescendants(aliveKnown.Select(process => process.ProcessId));
                    var discoveredIdentities = discoveredIds
                        .Select(_processTree.TryGetProcessIdentity)
                        .Where(identity => identity is not null)
                        .Select(identity => identity!)
                        .ToArray();
                    tracked.AddProcessIdentities(discoveredIdentities);
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
                        await FinalizeAsync(tracked, noProcessesSince.Value, failureMessage: null, cancellationToken);
                        return;
                    }
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when Grev Home shuts down.
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
        if (!_active.ContainsKey(tracked.LaunchSessionId))
        {
            return;
        }

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

    private async Task WaitForProcessesToExitAsync(
        TrackedLaunchSession tracked,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetValidatedProcessIds(tracked).Count == 0)
            {
                return;
            }

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
            if (snapshot.State != LaunchSessionState.Closing && GetValidatedProcessIds(tracked).Count > 0)
            {
                return tracked;
            }
        }

        return null;
    }

    private static bool ShouldReuseRunningSession(InstalledAppEntry entry)
    {
        var definition = entry.Manifest.Definition;
        return definition.Launch.SingleInstance ||
               (definition.InstallStrategy == InstallStrategy.SystemInstalled &&
                definition.Launch.DeclaredProcessNames.Count > 0);
    }

    private void RefreshDeclaredProcesses(TrackedLaunchSession tracked)
    {
        foreach (var processName in tracked.DeclaredProcessNames)
        {
            tracked.AddProcessIdentities(_processTree.GetProcessIdentitiesByName(processName));
        }
    }

    private static IReadOnlyList<string> GetDeclaredProcessNames(
        string? primary,
        IReadOnlyList<string>? additional) =>
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
            if (alive.Count == 0)
            {
                continue;
            }

            var duplicate = alive.Any(process =>
                claimedProcesses.Contains((tracked.AppId.ToUpperInvariant(), process.ProcessId)));
            if (duplicate)
            {
                changed |= _active.TryRemove(tracked.LaunchSessionId, out _);
                continue;
            }

            foreach (var process in alive)
            {
                claimedProcesses.Add((tracked.AppId.ToUpperInvariant(), process.ProcessId));
            }
        }

        if (changed)
        {
            PersistRuntimeState(force: true);
        }
    }

    private IReadOnlyList<RuntimeProcessIdentity> GetValidatedProcessIdentities(TrackedLaunchSession tracked) =>
        _processTree.GetAliveProcessIdentities(tracked.GetKnownProcessIdentities());

    private IReadOnlyList<int> GetValidatedProcessIds(TrackedLaunchSession tracked) =>
        GetValidatedProcessIdentities(tracked)
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
                        tracked.GetKnownProcessIdentities(),
                        tracked.ProcessName,
                        tracked.AdditionalProcessNames,
                        tracked.TrackDescendantProcesses,
                        tracked.ForceKillEntireProcessTree);
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
