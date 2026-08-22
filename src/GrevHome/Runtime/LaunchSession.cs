using GrevHome.Sessions;

namespace GrevHome.Runtime;

public enum LaunchSessionState
{
    Starting,
    Running,
    Closing,
    Exited,
    Failed
}

public sealed record LaunchParticipant(
    Guid SessionUserId,
    string? GrevId,
    string DisplayName,
    AccountKind AccountKind);

public sealed record RuntimeProcessIdentity(int ProcessId, DateTimeOffset StartedAtUtc);

public sealed record LaunchSessionSnapshot(
    Guid LaunchSessionId,
    string AppId,
    string AppName,
    string? PrimaryGrevId,
    IReadOnlyList<LaunchParticipant> Participants,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    LaunchSessionState State,
    int RootProcessId,
    IReadOnlyList<int> ProcessIds,
    string? FailureMessage,
    long? TrackedDurationSeconds = null)
{
    public TimeSpan Elapsed => TrackedDurationSeconds is long seconds
        ? TimeSpan.FromSeconds(Math.Max(0, seconds))
        : (EndedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc;
}

internal sealed class TrackedLaunchSession
{
    private readonly object _gate = new();
    private readonly Dictionary<int, DateTimeOffset> _processes = new();
    private long _accumulatedSuspendedSeconds;
    private DateTimeOffset? _suspendedAtUtc;

    public Guid LaunchSessionId { get; }
    public string AppId { get; }
    public string AppName { get; }
    public string? PrimaryGrevId { get; }
    public string? ProcessName { get; }
    public IReadOnlyList<string> AdditionalProcessNames { get; }
    public bool TrackDescendantProcesses { get; }
    public bool ForceKillEntireProcessTree { get; }
    public IReadOnlyList<LaunchParticipant> Participants { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset LastObservedAliveAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public LaunchSessionState State { get; private set; }
    public int RootProcessId { get; }
    public string? FailureMessage { get; private set; }

    public long AccumulatedSuspendedSeconds
    {
        get { lock (_gate) return _accumulatedSuspendedSeconds; }
    }

    public DateTimeOffset? SuspendedAtUtc
    {
        get { lock (_gate) return _suspendedAtUtc; }
    }

    public IReadOnlyList<string> DeclaredProcessNames =>
        new[] { ProcessName }
            .Concat(AdditionalProcessNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public TrackedLaunchSession(
        string appId,
        string appName,
        string? primaryGrevId,
        string? processName,
        IReadOnlyList<string>? additionalProcessNames,
        bool trackDescendantProcesses,
        bool forceKillEntireProcessTree,
        IReadOnlyList<LaunchParticipant> participants,
        RuntimeProcessIdentity rootProcess,
        DateTimeOffset startedAtUtc)
        : this(
            Guid.NewGuid(),
            appId,
            appName,
            primaryGrevId,
            processName,
            additionalProcessNames,
            trackDescendantProcesses,
            forceKillEntireProcessTree,
            participants,
            rootProcess.ProcessId,
            new[] { rootProcess },
            startedAtUtc,
            startedAtUtc,
            LaunchSessionState.Running,
            0,
            null)
    {
    }

    private TrackedLaunchSession(
        Guid launchSessionId,
        string appId,
        string appName,
        string? primaryGrevId,
        string? processName,
        IReadOnlyList<string>? additionalProcessNames,
        bool trackDescendantProcesses,
        bool forceKillEntireProcessTree,
        IReadOnlyList<LaunchParticipant> participants,
        int rootProcessId,
        IReadOnlyList<RuntimeProcessIdentity> processes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastObservedAliveAtUtc,
        LaunchSessionState state,
        long accumulatedSuspendedSeconds,
        DateTimeOffset? suspendedAtUtc)
    {
        LaunchSessionId = launchSessionId;
        AppId = appId;
        AppName = appName;
        PrimaryGrevId = primaryGrevId;
        ProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        AdditionalProcessNames = (additionalProcessNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !string.Equals(name, ProcessName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        TrackDescendantProcesses = trackDescendantProcesses;
        ForceKillEntireProcessTree = forceKillEntireProcessTree;
        Participants = participants;
        RootProcessId = rootProcessId;
        StartedAtUtc = startedAtUtc;
        LastObservedAliveAtUtc = lastObservedAliveAtUtc;
        State = state is LaunchSessionState.Closing ? LaunchSessionState.Closing : LaunchSessionState.Running;
        _accumulatedSuspendedSeconds = Math.Max(0, accumulatedSuspendedSeconds);
        _suspendedAtUtc = suspendedAtUtc;

        foreach (var process in processes.Where(process => process.ProcessId > 0))
        {
            _processes[process.ProcessId] = process.StartedAtUtc;
        }
    }

    public static TrackedLaunchSession Recover(
        Guid launchSessionId,
        string appId,
        string appName,
        string? primaryGrevId,
        string? processName,
        IReadOnlyList<string>? additionalProcessNames,
        bool trackDescendantProcesses,
        bool forceKillEntireProcessTree,
        IReadOnlyList<LaunchParticipant> participants,
        int rootProcessId,
        IReadOnlyList<RuntimeProcessIdentity> processes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastObservedAliveAtUtc,
        LaunchSessionState state,
        long accumulatedSuspendedSeconds = 0,
        DateTimeOffset? suspendedAtUtc = null) =>
        new(
            launchSessionId,
            appId,
            appName,
            primaryGrevId,
            processName,
            additionalProcessNames,
            trackDescendantProcesses,
            forceKillEntireProcessTree,
            participants,
            rootProcessId,
            processes,
            startedAtUtc,
            lastObservedAliveAtUtc,
            state,
            accumulatedSuspendedSeconds,
            suspendedAtUtc);

    public IReadOnlyList<int> GetKnownProcessIds()
    {
        lock (_gate)
        {
            return _processes.Keys.OrderBy(id => id).ToArray();
        }
    }

    public IReadOnlyList<RuntimeProcessIdentity> GetKnownProcessIdentities()
    {
        lock (_gate)
        {
            return _processes
                .OrderBy(pair => pair.Key)
                .Select(pair => new RuntimeProcessIdentity(pair.Key, pair.Value))
                .ToArray();
        }
    }

    public void AddProcessIdentities(IEnumerable<RuntimeProcessIdentity> processes)
    {
        lock (_gate)
        {
            foreach (var process in processes.Where(process => process.ProcessId > 0))
            {
                _processes.TryAdd(process.ProcessId, process.StartedAtUtc);
            }
        }
    }

    public void MarkObservedAlive(DateTimeOffset observedAtUtc)
    {
        lock (_gate)
        {
            if (EndedAtUtc is null && observedAtUtc > LastObservedAliveAtUtc)
            {
                LastObservedAliveAtUtc = observedAtUtc;
            }
        }
    }

    public void MarkSuspended(DateTimeOffset suspendedAtUtc)
    {
        lock (_gate)
        {
            if (EndedAtUtc is null && _suspendedAtUtc is null)
            {
                _suspendedAtUtc = suspendedAtUtc;
            }
        }
    }

    public void MarkResumed(DateTimeOffset resumedAtUtc)
    {
        lock (_gate)
        {
            if (_suspendedAtUtc is not { } suspendedAt)
            {
                return;
            }

            if (resumedAtUtc > suspendedAt)
            {
                _accumulatedSuspendedSeconds = checked(
                    _accumulatedSuspendedSeconds +
                    Math.Max(0L, (long)Math.Round((resumedAtUtc - suspendedAt).TotalSeconds)));
            }
            _suspendedAtUtc = null;
        }
    }

    public long GetTrackedDurationSeconds(DateTimeOffset throughUtc)
    {
        lock (_gate)
        {
            return CalculateTrackedDurationSecondsLocked(throughUtc);
        }
    }

    public void MarkClosing()
    {
        lock (_gate)
        {
            if (EndedAtUtc is null)
            {
                State = LaunchSessionState.Closing;
            }
        }
    }

    public void MarkExited(DateTimeOffset endedAtUtc)
    {
        lock (_gate)
        {
            if (EndedAtUtc is not null)
            {
                return;
            }

            EndedAtUtc = endedAtUtc;
            State = LaunchSessionState.Exited;
        }
    }

    public void MarkFailed(string failureMessage, DateTimeOffset endedAtUtc)
    {
        lock (_gate)
        {
            FailureMessage = failureMessage;
            EndedAtUtc = endedAtUtc;
            State = LaunchSessionState.Failed;
        }
    }

    public LaunchSessionSnapshot Snapshot()
    {
        lock (_gate)
        {
            var effectiveEnd = EndedAtUtc ?? DateTimeOffset.UtcNow;
            var trackedSeconds = CalculateTrackedDurationSecondsLocked(effectiveEnd);

            return new LaunchSessionSnapshot(
                LaunchSessionId,
                AppId,
                AppName,
                PrimaryGrevId,
                Participants,
                StartedAtUtc,
                EndedAtUtc,
                State,
                RootProcessId,
                _processes.Keys.OrderBy(id => id).ToArray(),
                FailureMessage,
                trackedSeconds);
        }
    }

    private long CalculateTrackedDurationSecondsLocked(DateTimeOffset throughUtc)
    {
        var effectiveEnd = throughUtc < StartedAtUtc ? StartedAtUtc : throughUtc;
        var suspendedSeconds = _accumulatedSuspendedSeconds;
        if (_suspendedAtUtc is { } suspendedAt && effectiveEnd > suspendedAt)
        {
            suspendedSeconds = checked(
                suspendedSeconds +
                Math.Max(0L, (long)Math.Round((effectiveEnd - suspendedAt).TotalSeconds)));
        }

        var rawSeconds = Math.Max(0L, (long)Math.Round((effectiveEnd - StartedAtUtc).TotalSeconds));
        return Math.Max(0L, rawSeconds - suspendedSeconds);
    }
}
