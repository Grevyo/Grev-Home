using GrevHome.Runtime;

namespace GrevHome;

public partial class MainWindow
{
    private SessionHistoryService? _sessionHistory;
    private bool _sessionHistoryIntegrationReady;

    private void InitializeSessionHistoryIntegration()
    {
        if (_sessionHistoryIntegrationReady)
        {
            return;
        }

        _sessionHistoryIntegrationReady = true;
        _sessionHistory = new SessionHistoryService(_paths);
        _runtimeSessions.SessionEnded += HandleLocalSessionHistoryEnded;
    }

    private void HandleLocalSessionHistoryEnded(LaunchSessionSnapshot snapshot) =>
        _ = RecordLocalSessionHistorySafeAsync(snapshot);

    private async Task RecordLocalSessionHistorySafeAsync(LaunchSessionSnapshot snapshot)
    {
        var history = _sessionHistory;
        if (history is null)
        {
            return;
        }

        try
        {
            await history.RecordAsync(snapshot);

            // Grev.dad is an optional mirror. Only offer the completed session for online sync
            // after the durable GrevID-owned local journal has committed it successfully.
            QueueGrevDadSyncAfterLocalHistory(snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
        {
            // Session history is additive metadata. Runtime completion and aggregate playtime have
            // already succeeded and must never be rolled back because this append failed.
        }
    }
}
