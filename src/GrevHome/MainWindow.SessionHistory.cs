using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private readonly SessionHistoryService _sessionHistory;
    private bool _sessionHistoryIntegrationReady;

    private void InitializeSessionHistoryIntegration()
    {
        if (_sessionHistoryIntegrationReady)
        {
            return;
        }

        _sessionHistoryIntegrationReady = true;

        // RuntimeSessionManager owns local completion durability. Grev.dad is only offered a
        // completed session after both the idempotent playtime aggregate and immutable local
        // history have committed and the pending completion envelope has been cleared.
        _runtimeSessions.SessionHistoryCommitted += QueueGrevDadSyncAfterLocalHistory;
    }
}
