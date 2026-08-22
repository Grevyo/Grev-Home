using System.IO;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Notifications;
using GrevHome.Transfers;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ActivityCenterView _activityCenterView = new();
    private NotificationService? _notificationService;
    private TransferManager? _transferManager;
    private bool _activityCenterHooked;
    private int _activityCenterRefreshGeneration;

    private void InitializeActivityCenterIntegration()
    {
        if (_activityCenterHooked)
        {
            return;
        }

        _activityCenterHooked = true;
        _notificationService = new NotificationService(_paths);
        _transferManager = new TransferManager(_paths, _notificationService);

        _dashboardView.ActivityCenterRequested += (_, _) => OpenActivityCenter();
        _activityCenterView.BackRequested += (_, _) => _navigation.GoBack();
        _activityCenterView.MarkAllNotificationsReadRequested += (_, _) => _ = MarkAllActivityNotificationsReadAsync();
        _activityCenterView.NotificationReadRequested += id => _ = MarkActivityNotificationReadAsync(id);
        _activityCenterView.TransferCancelRequested += id => _ = CancelActivityTransferAsync(id);
        _activityCenterView.TransferRetryRequested += id => _ = RetryActivityTransferAsync(id);
        _activityCenterView.ClearFinishedTransfersRequested += (_, _) => _ = ClearFinishedActivityTransfersAsync();

        _notificationService.Changed += QueueActivityCenterRefresh;
        _transferManager.SnapshotChanged += _ => QueueActivityCenterRefresh();
        _session.Changed += (_, _) => QueueActivityCenterRefresh();
        _navigation.RouteChanged += route =>
        {
            if (route == Route.ActivityCenter)
            {
                RouteHost.Content = _activityCenterView;
                QueueActivityCenterRefresh();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(_activityCenterView.FocusInitial));
            }
            else if (route == Route.Dashboard)
            {
                QueueActivityCenterRefresh();
            }
        };

        Closed += (_, _) => _transferManager?.Dispose();
        _ = InitializeActivityCenterAsync();
    }

    private async Task InitializeActivityCenterAsync()
    {
        var transfers = _transferManager;
        if (transfers is null)
        {
            return;
        }

        try
        {
            await transfers.InitializeAsync();
            await RefreshActivityCenterAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _dashboardView.ShowStatus($"Activity Center could not initialize: {ex.Message}");
        }
    }

    private void OpenActivityCenter()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        _activityCenterView.ShowStatus(string.Empty);
        _navigation.Navigate(Route.ActivityCenter);
    }

    private void QueueActivityCenterRefresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(QueueActivityCenterRefresh));
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => _ = RefreshActivityCenterAsync()));
    }

    private async Task RefreshActivityCenterAsync()
    {
        var notifications = _notificationService;
        var transfers = _transferManager;
        if (notifications is null || transfers is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _activityCenterRefreshGeneration);
        var grevId = _session.PrimaryUser?.GrevId;

        try
        {
            var notificationTask = notifications.GetForGrevIdAsync(grevId, maximumItems: 20);
            var transferTask = transfers.GetSnapshotAsync();
            await Task.WhenAll(notificationTask, transferTask);

            if (generation != Volatile.Read(ref _activityCenterRefreshGeneration))
            {
                return;
            }

            var notificationSnapshot = await notificationTask;
            var transferSnapshot = await transferTask;
            _dashboardView.SetSystemActivity(notificationSnapshot, transferSnapshot);
            _activityCenterView.SetData(notificationSnapshot, grevId, transferSnapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (generation != Volatile.Read(ref _activityCenterRefreshGeneration))
            {
                return;
            }

            _dashboardView.SetSystemActivity(NotificationSnapshot.Empty, TransferSnapshot.Empty);
            if (_navigation.Current == Route.ActivityCenter)
            {
                _activityCenterView.ShowStatus($"Activity could not be refreshed: {ex.Message}");
            }
        }
    }

    private async Task MarkActivityNotificationReadAsync(string notificationId)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        var notifications = _notificationService;
        if (notifications is null || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        await notifications.MarkReadAsync(notificationId, grevId);
    }

    private async Task MarkAllActivityNotificationsReadAsync()
    {
        var grevId = _session.PrimaryUser?.GrevId;
        var notifications = _notificationService;
        if (notifications is null || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        await notifications.MarkAllReadAsync(grevId);
    }

    private async Task CancelActivityTransferAsync(string transferId)
    {
        if (_transferManager is null)
        {
            return;
        }

        try
        {
            await _transferManager.CancelAsync(transferId);
            _activityCenterView.ShowStatus("Transfer cancellation requested.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _activityCenterView.ShowStatus($"Transfer could not be cancelled: {ex.Message}");
        }
    }

    private async Task RetryActivityTransferAsync(string transferId)
    {
        if (_transferManager is null)
        {
            return;
        }

        try
        {
            await _transferManager.RetryAsync(transferId);
            _activityCenterView.ShowStatus("Transfer queued for retry.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _activityCenterView.ShowStatus($"Transfer could not be retried: {ex.Message}");
        }
    }

    private async Task ClearFinishedActivityTransfersAsync()
    {
        if (_transferManager is null)
        {
            return;
        }

        try
        {
            await _transferManager.ClearFinishedAsync();
            _activityCenterView.ShowStatus("Finished transfer history cleared.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _activityCenterView.ShowStatus($"Finished transfers could not be cleared: {ex.Message}");
        }
    }

    /// <summary>
    /// Shared package/runtime entry point. Future features publish into the same persistent feed
    /// rather than creating their own notification storage or dashboard-only banners.
    /// </summary>
    private async Task PublishActivityNotificationAsync(
        NotificationSeverity severity,
        string source,
        string title,
        string message,
        string? grevId = null,
        CancellationToken cancellationToken = default)
    {
        if (_notificationService is null)
        {
            return;
        }

        await _notificationService.PublishAsync(
            severity,
            source,
            title,
            message,
            grevId,
            cancellationToken);
    }

    /// <summary>
    /// Shared trusted-package entry point for queued downloads. Package handlers supply a relative
    /// destination only; TransferManager enforces that the resolved file remains under Downloads.
    /// </summary>
    private Task<TransferItem> QueueDownloadAsync(
        Uri source,
        string relativeDestination,
        string displayName,
        string? ownerGrevId = null,
        CancellationToken cancellationToken = default)
    {
        var transfers = _transferManager
            ?? throw new InvalidOperationException("Transfer manager has not been initialized.");
        return transfers.EnqueueDownloadAsync(
            source,
            relativeDestination,
            displayName,
            ownerGrevId,
            cancellationToken);
    }
}
