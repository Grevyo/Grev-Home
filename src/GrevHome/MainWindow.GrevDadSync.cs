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

        // A successful/revalidated link backfills legacy playtime and any locally queued completed
        // sessions. Unlinked/offline local accounts continue without entering this path.
        accounts.SnapshotChanged += (grevId, snapshot) =>
        {
            if (snapshot.State == GrevDadConnectionState.Linked)
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
