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
    private readonly HashSet<int> _processIds = new();

    public Guid LaunchSessionId { get; } = Guid.NewGuid();
    public string AppId { get; }
    public string AppName { get; }
    public string? PrimaryGrevId { get; }
    public IReadOnlyList<LaunchParticipant> Participants { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public LaunchSessionState State { get; private set; }
    public int RootProcessId { get; }
    public string? FailureMessage { get; private set; }

    public TrackedLaunchSession(
        string appId,
        string appName,
        string? primaryGrevId,
        IReadOnlyList<LaunchParticipant> participants,
        int rootProcessId,
        DateTimeOffset startedAtUtc)
    {
        AppId = appId;
        AppName = appName;
        PrimaryGrevId = primaryGrevId;
        Participants = participants;
        RootProcessId = rootProcessId;
        StartedAtUtc = startedAtUtc;
        State = LaunchSessionState.Running;
        _processIds.Add(rootProcessId);
    }

    public IReadOnlyList<int> GetKnownProcessIds()
    {
        lock (_gate)
        {
            return _processIds.OrderBy(id => id).ToArray();
        }
    }

    public void AddProcessIds(IEnumerable<int> processIds)
    {
        lock (_gate)
        {
            foreach (var processId in processIds.Where(id => id > 0))
            {
                _processIds.Add(processId);
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
                _processIds.OrderBy(id => id).ToArray(),
                FailureMessage);
        }
    }
}
