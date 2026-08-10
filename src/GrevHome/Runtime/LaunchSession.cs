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
    string? FailureMessage)
{
    public TimeSpan Elapsed => (EndedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc;
}

internal sealed class TrackedLaunchSession
{
    private readonly object _gate = new();
    private readonly Dictionary<int, DateTimeOffset> _processes = new();

    public Guid LaunchSessionId { get; }
    public string AppId { get; }
    public string AppName { get; }
    public string? PrimaryGrevId { get; }
    public IReadOnlyList<LaunchParticipant> Participants { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset LastObservedAliveAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public LaunchSessionState State { get; private set; }
    public int RootProcessId { get; }
    public string? FailureMessage { get; private set; }

    public TrackedLaunchSession(
        string appId,
        string appName,
        string? primaryGrevId,
        IReadOnlyList<LaunchParticipant> participants,
        RuntimeProcessIdentity rootProcess,
        DateTimeOffset startedAtUtc)
        : this(
            Guid.NewGuid(),
            appId,
            appName,
            primaryGrevId,
            participants,
            rootProcess.ProcessId,
            new[] { rootProcess },
            startedAtUtc,
            startedAtUtc,
            LaunchSessionState.Running)
    {
    }

    private TrackedLaunchSession(
        Guid launchSessionId,
        string appId,
        string appName,
        string? primaryGrevId,
        IReadOnlyList<LaunchParticipant> participants,
        int rootProcessId,
        IReadOnlyList<RuntimeProcessIdentity> processes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastObservedAliveAtUtc,
        LaunchSessionState state)
    {
        LaunchSessionId = launchSessionId;
        AppId = appId;
        AppName = appName;
        PrimaryGrevId = primaryGrevId;
        Participants = participants;
        RootProcessId = rootProcessId;
        StartedAtUtc = startedAtUtc;
        LastObservedAliveAtUtc = lastObservedAliveAtUtc;
        State = state is LaunchSessionState.Closing ? LaunchSessionState.Closing : LaunchSessionState.Running;

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
        IReadOnlyList<LaunchParticipant> participants,
        int rootProcessId,
        IReadOnlyList<RuntimeProcessIdentity> processes,
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastObservedAliveAtUtc,
        LaunchSessionState state) =>
        new(
            launchSessionId,
            appId,
            appName,
            primaryGrevId,
            participants,
            rootProcessId,
            processes,
            startedAtUtc,
            lastObservedAliveAtUtc,
            state);

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
                FailureMessage);
        }
    }
}
