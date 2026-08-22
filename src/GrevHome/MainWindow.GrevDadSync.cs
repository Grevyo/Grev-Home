using GrevHome.Online;
using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private GrevDadProfileSyncService? _grevDadProfileSync;
    private bool _grevDadProfileSyncReady;

    private void InitializeGrevDadProfileSyncIntegration()
    {
        if (_grevDadProfileSyncReady)
        {
            return;
        }

        var history = _sessionHistory
            ?? throw new InvalidOperationException("Local session history must initialize before Grev.dad sync.");
        var accounts = RequireGrevDadAccountService();

        _grevDadProfileSyncReady = true;
        _grevDadProfileSync = new GrevDadProfileSyncService(_paths, history, accounts);

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

        Closed += (_, _) => _grevDadProfileSync?.Dispose();
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
        if (sync is null)
        {
            return;
        }

        try
        {
            await sync.SyncAsync(grevId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                   ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            // The local journal is the queue. A failed upload leaves the sync cursor unchanged, so
            // a future valid link/network connection can replay the same immutable records safely.
        }
    }
}
