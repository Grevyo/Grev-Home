using System.IO;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Notifications;
using GrevHome.Runtime;
using GrevHome.Store.Installers;
using GrevHome.Transfers;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ActivityCenterView _activityCenterView = new();
    private NotificationService? _notificationService;
    private TransferManager? _transferManager;
    private TrustedPackageDownloadService? _packageDownloadService;
    private bool _activityInfrastructureReady;
    private bool _activityCenterHooked;
    private int _activityCenterRefreshGeneration;

    /// <summary>
    /// Creates the durable activity/transfer services without depending on the Activity Center UI.
    /// Grev Store calls this while building trusted installers so their real package downloads can
    /// use the same queue that Activity Center later renders.
    /// </summary>
    private void EnsureActivityInfrastructure()
    {
        if (_activityInfrastructureReady)
        {
            return;
        }

        _activityInfrastructureReady = true;
        _notificationService = new NotificationService(_paths);
        _transferManager = new TransferManager(_paths, _notificationService);
        _packageDownloadService = new TrustedPackageDownloadService(_paths, _transferManager);
        Closed += (_, _) => _transferManager?.Dispose();
    }

    private TrustedPackageDownloadService GetTrustedPackageDownloadService()
    {
        EnsureActivityInfrastructure();
        return _packageDownloadService
               ?? throw new InvalidOperationException("Trusted package download service was not initialized.");
    }

    private void InitializeActivityCenterIntegration()
    {
        if (_activityCenterHooked)
        {
            return;
        }

        EnsureActivityInfrastructure();
        _activityCenterHooked = true;

        var notifications = _notificationService
            ?? throw new InvalidOperationException("Notification service was not initialized.");
        var transfers = _transferManager
            ?? throw new InvalidOperationException("Transfer manager was not initialized.");

        _dashboardView.ActivityCenterRequested += (_, _) => OpenActivityCenter();
        _activityCenterView.BackRequested += (_, _) => _navigation.GoBack();
        _activityCenterView.MarkAllNotificationsReadRequested += (_, _) => _ = MarkAllActivityNotificationsReadAsync();
        _activityCenterView.NotificationReadRequested += id => _ = MarkActivityNotificationReadAsync(id);
        _activityCenterView.TransferCancelRequested += id => _ = CancelActivityTransferAsync(id);
        _activityCenterView.TransferRetryRequested += id => _ = RetryActivityTransferAsync(id);
        _activityCenterView.ClearFinishedTransfersRequested += (_, _) => _ = ClearFinishedActivityTransfersAsync();

        notifications.Changed += QueueActivityCenterRefresh;
        transfers.SnapshotChanged += _ => QueueActivityCenterRefresh();
        _session.Changed += (_, _) => QueueActivityCenterRefresh();

        // Grev Store is initialized before Activity Center in the explicit shell bootstrap, so the
        // registry is available here. Store and Admin Console both use this registry, which gives
        // Activity Center one package-operation event stream instead of UI-specific hooks.
        if (_packageInstallers is not null)
        {
            _packageInstallers.OperationCompleted += result => _ = PublishPackageOperationResultAsync(result);
        }

        _runtimeSessions.SessionEnded += snapshot =>
        {
            if (snapshot.State == LaunchSessionState.Failed)
            {
                _ = PublishRuntimeFailureAsync(snapshot);
            }
        };

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

    private async Task PublishPackageOperationResultAsync(TrustedPackageOperationResult result)
    {
        var action = result.Operation switch
        {
            TrustedPackageOperationKind.Install => "install",
            TrustedPackageOperationKind.Update => "update",
            TrustedPackageOperationKind.Repair => "repair",
            TrustedPackageOperationKind.Uninstall => "uninstall",
            _ => "operation"
        };
        var displayName = result.Package.Presentation.DisplayName;

        if (result.Succeeded)
        {
            await TryPublishActivityNotificationAsync(
                NotificationSeverity.Success,
                "Apps",
                $"{displayName} {action} complete",
                $"The trusted {action} operation completed successfully.",
                result.GrevId);
            return;
        }

        var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "The trusted package operation did not complete."
            : result.ErrorMessage;
        await TryPublishActivityNotificationAsync(
            NotificationSeverity.Error,
            "Apps",
            $"{displayName} {action} failed",
            LimitNotificationMessage(detail),
            result.GrevId);
    }

    private async Task PublishRuntimeFailureAsync(LaunchSessionSnapshot snapshot)
    {
        var detail = string.IsNullOrWhiteSpace(snapshot.FailureMessage)
            ? "The managed app session ended in a failed state."
            : snapshot.FailureMessage;
        await TryPublishActivityNotificationAsync(
            NotificationSeverity.Error,
            "Runtime",
            $"{snapshot.AppName} stopped unexpectedly",
            LimitNotificationMessage(detail),
            snapshot.PrimaryGrevId);
    }

    private async Task TryPublishActivityNotificationAsync(
        NotificationSeverity severity,
        string source,
        string title,
        string message,
        string? grevId = null)
    {
        try
        {
            await PublishActivityNotificationAsync(severity, source, title, message, grevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // A status feed must never turn a completed package/runtime action into an app failure.
        }
    }

    private static string LimitNotificationMessage(string message) =>
        message.Length <= 1000 ? message : message[..997] + "...";

    /// <summary>
    /// Shared package/runtime entry point. Features publish into the same persistent feed rather
    /// than creating their own notification storage or dashboard-only banners.
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
        EnsureActivityInfrastructure();
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
