using System.Collections.Concurrent;
using System.Diagnostics;
using GrevHome.Apps;
using GrevHome.Sessions;

namespace GrevHome.Runtime;

public sealed class RuntimeSessionManager : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<Guid, TrackedLaunchSession> _active = new();
    private readonly ProcessTreeService _processTree;
    private readonly PlaytimeService _playtime;
    private readonly AppLaunchResolver _launchResolver;
    private readonly CancellationTokenSource _shutdown = new();

    public event Action<LaunchSessionSnapshot>? SessionChanged;
    public event Action<LaunchSessionSnapshot>? SessionEnded;

    public RuntimeSessionManager(
        ProcessTreeService processTree,
        PlaytimeService playtime,
        AppLaunchResolver launchResolver)
    {
        _processTree = processTree;
        _playtime = playtime;
        _launchResolver = launchResolver;
    }

    public IReadOnlyList<LaunchSessionSnapshot> GetActiveSessions() =>
        _active.Values
            .Select(session => session.Snapshot())
            .OrderByDescending(session => session.StartedAtUtc)
            .ToArray();

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

        var startInfo = _launchResolver.Resolve(entry, primary.GrevId);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Windows did not start {entry.Manifest.Definition.Name}.");

        var tracked = new TrackedLaunchSession(
            entry.Manifest.Definition.AppId,
            entry.Manifest.Definition.Name,
            primary.GrevId,
            participants,
            process.Id,
            DateTimeOffset.UtcNow);

        if (!_active.TryAdd(tracked.LaunchSessionId, tracked))
        {
            throw new InvalidOperationException("Grev Home could not register the new runtime session.");
        }

        var snapshot = tracked.Snapshot();
        SessionChanged?.Invoke(snapshot);
        _ = Task.Run(() => MonitorAsync(tracked, _shutdown.Token), CancellationToken.None);
        return Task.FromResult(snapshot);
    }

    private async Task MonitorAsync(TrackedLaunchSession tracked, CancellationToken cancellationToken)
    {
        DateTimeOffset? noProcessesSince = null;
        var lastKnownCount = tracked.GetKnownProcessIds().Count;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var known = tracked.GetKnownProcessIds();
                var discovered = _processTree.DiscoverDescendants(known);
                tracked.AddProcessIds(discovered);

                var currentKnown = tracked.GetKnownProcessIds();
                if (currentKnown.Count != lastKnownCount)
                {
                    lastKnownCount = currentKnown.Count;
                    SessionChanged?.Invoke(tracked.Snapshot());
                }

                var alive = _processTree.GetAliveProcessIds(currentKnown);
                if (alive.Count > 0)
                {
                    noProcessesSince = null;
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
            // Grev Home is shutting down. A later recovery milestone will persist active runtime state.
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
        SessionChanged?.Invoke(snapshot);
        SessionEnded?.Invoke(snapshot);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
