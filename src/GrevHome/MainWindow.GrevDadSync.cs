using System.Collections.Concurrent;
using System.Windows.Threading;
using GrevHome.Online;
using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private sealed record GrevDadSyncRetryState(int FailureCount, DateTimeOffset NextAttemptAtUtc);

    private static readonly TimeSpan[] GrevDadSyncRetryBackoff =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    private readonly ConcurrentDictionary<string, GrevDadSyncRetryState> _grevDadSyncRetries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _grevDadSyncRetryTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };

    private GrevDadProfileSyncService? _grevDadProfileSync;
    private bool _grevDadProfileSyncReady;
    private int _grevDadSyncRetryTickActive;

    private void InitializeGrevDadProfileSyncIntegration()
    {
        if (_grevDadProfileSyncReady)
        {
            return;
        }

        var history = _sessionHistory
            ?? throw new InvalidOperationException("Local session history must initialize before Grev.dad sync.");
        var accounts = RequireGrevDadAccountService();
        var privacy = RequireGrevDadPrivacySettingsService();

        _grevDadProfileSyncReady = true;
        _grevDadProfileSync = new GrevDadProfileSyncService(_paths, history, accounts, privacy);
        _grevDadSyncRetryTimer.Tick += (_, _) => _ = RetryDueGrevDadSyncsAsync();

        // Session changes are an explicit lifecycle edge. They can backfill a linked Primary GrevID
        // after Grev Home starts without coupling sync to account SnapshotChanged; routine account
        // revalidation itself publishes snapshots and must never recursively schedule another sync.
        _session.Changed += (_, _) =>
        {
            var grevId = _session.PrimaryUser?.GrevId;
            if (!string.IsNullOrWhiteSpace(grevId))
            {
                _ = SyncGrevDadProfileSafeAsync(grevId);
            }
        };

        Closed += (_, _) =>
        {
            _grevDadSyncRetryTimer.Stop();
            _grevDadSyncRetries.Clear();
            _grevDadProfileSync?.Dispose();
        };
    }

    private void QueueGrevDadSyncAfterLocalHistory(LaunchSessionSnapshot snapshot)
    {
        var grevIds = snapshot.Participants
            .Select(participant => participant.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var grevId in grevIds)
        {
            _ = SyncGrevDadProfileSafeAsync(grevId);
        }
    }

    private async Task SyncGrevDadProfileSafeAsync(string grevId)
    {
        var sync = _grevDadProfileSync;
        var accounts = _grevDadAccounts;
        if (sync is null || accounts is null || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        try
        {
            var result = await sync.SyncAsync(grevId);
            if (result is not null)
            {
                if (result.HasMoreHistory)
                {
                    ScheduleGrevDadSyncContinuation(grevId);
                }
                else
                {
                    // Refresh other devices' account statistics without requiring a restart.
                    _grevDadSyncRetries[grevId] = new GrevDadSyncRetryState(0,DateTimeOffset.UtcNow+TimeSpan.FromMinutes(2));
                    EnsureGrevDadSyncRetryTimerRunning();
                }
                if (string.Equals(GetProfileTarget()?.GrevId,grevId,StringComparison.OrdinalIgnoreCase))
                    await LoadProfileStatsAsync(grevId);
                return;
            }

            // ValidateLinkedAccountAsync deliberately converts transport failures to Offline rather
            // than throwing. Preserve eventual delivery by retrying only that state. Unlinked,
            // expired and revoked profiles must never be kept alive by a background retry loop.
            var state = accounts.GetLastSnapshot(grevId).State;
            if (state == GrevDadConnectionState.Offline)
            {
                ScheduleGrevDadSyncRetry(grevId);
            }
            else if (state is GrevDadConnectionState.Unlinked or
                     GrevDadConnectionState.Expired or
                     GrevDadConnectionState.Revoked or
                     GrevDadConnectionState.Error)
            {
                ClearGrevDadSyncRetry(grevId);
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                   ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            // The local journal remains the durable queue. Failed transport never advances its
            // cursor; a bounded backoff retries linked/offline profiles without blocking the shell.
            ScheduleGrevDadSyncRetry(grevId);
        }
    }

    private void ScheduleGrevDadSyncContinuation(string grevId)
    {
        // A successful run is intentionally capped at 1,000 history rows. If more local history
        // remains, schedule another bounded pass without treating healthy backlog as a failure.
        _grevDadSyncRetries[grevId] = new GrevDadSyncRetryState(
            0,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        EnsureGrevDadSyncRetryTimerRunning();
    }

    private void ScheduleGrevDadSyncRetry(string grevId)
    {
        _grevDadSyncRetries.AddOrUpdate(
            grevId,
            _ => new GrevDadSyncRetryState(1, DateTimeOffset.UtcNow + GrevDadSyncRetryBackoff[0]),
            (_, existing) =>
            {
                var failures = Math.Min(existing.FailureCount + 1, GrevDadSyncRetryBackoff.Length);
                var delay = GrevDadSyncRetryBackoff[Math.Min(failures - 1, GrevDadSyncRetryBackoff.Length - 1)];
                return new GrevDadSyncRetryState(failures, DateTimeOffset.UtcNow + delay);
            });

        EnsureGrevDadSyncRetryTimerRunning();
    }

    private void ClearGrevDadSyncRetry(string grevId)
    {
        _grevDadSyncRetries.TryRemove(grevId, out _);
        if (_grevDadSyncRetries.IsEmpty)
        {
            StopGrevDadSyncRetryTimer();
        }
    }

    private async Task RetryDueGrevDadSyncsAsync()
    {
        if (Interlocked.Exchange(ref _grevDadSyncRetryTickActive, 1) != 0)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var due = _grevDadSyncRetries
                .Where(pair => pair.Value.NextAttemptAtUtc <= now)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var grevId in due)
            {
                await SyncGrevDadProfileSafeAsync(grevId);
            }

            if (_grevDadSyncRetries.IsEmpty)
            {
                StopGrevDadSyncRetryTimer();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _grevDadSyncRetryTickActive, 0);
        }
    }

    private void EnsureGrevDadSyncRetryTimerRunning()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(EnsureGrevDadSyncRetryTimerRunning));
            return;
        }

        if (!_grevDadSyncRetryTimer.IsEnabled)
        {
            _grevDadSyncRetryTimer.Start();
        }
    }

    private void StopGrevDadSyncRetryTimer()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(StopGrevDadSyncRetryTimer));
            return;
        }

        if (_grevDadSyncRetryTimer.IsEnabled)
        {
            _grevDadSyncRetryTimer.Stop();
        }
    }
}
