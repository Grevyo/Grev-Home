using System.IO;
using System.Windows.Threading;
using GrevHome.Dashboard;
using GrevHome.Navigation;

namespace GrevHome;

public partial class MainWindow
{
    private DashboardDataService? _dashboardDataService;
    private bool _dashboardDataHooked;
    private int _dashboardDataRefreshGeneration;

    private void InitializeDashboardDataIntegration()
    {
        if (_dashboardDataHooked)
        {
            return;
        }

        _dashboardDataHooked = true;
        _dashboardDataService = new DashboardDataService(_paths, _installedApps);

        // Activity Center is the status/transfer half of the Dashboard/Data backbone. Keep its
        // bootstrap adjacent to dashboard activity so the shell still has one explicit backbone
        // entry point rather than feature views initializing services themselves.
        InitializeActivityCenterIntegration();

        _dashboardView.ActivityAppRequested += appId => _ = LaunchDashboardActivityAppAsync(appId);
        _navigation.RouteChanged += route =>
        {
            if (route == Route.Dashboard)
            {
                QueueDashboardDataRefresh();
            }
        };
        _session.Changed += (_, _) => QueueDashboardDataRefresh();
        _runtimeSessions.SessionEnded += _ => QueueDashboardDataRefresh();

        QueueDashboardDataRefresh();
    }

    private void QueueDashboardDataRefresh()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => _ = RefreshDashboardDataAsync()));
    }

    private async Task RefreshDashboardDataAsync()
    {
        var service = _dashboardDataService;
        if (service is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _dashboardDataRefreshGeneration);
        var grevId = _session.PrimaryUser?.GrevId;

        try
        {
            var snapshot = await service.GetForGrevIdAsync(grevId);
            if (generation != Volatile.Read(ref _dashboardDataRefreshGeneration))
            {
                return;
            }

            _dashboardView.SetDashboardData(snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (generation != Volatile.Read(ref _dashboardDataRefreshGeneration))
            {
                return;
            }

            _dashboardView.SetDashboardData(DashboardDataSnapshot.Empty);
            _dashboardView.ShowStatus($"Dashboard activity could not be refreshed: {ex.Message}");
        }
    }

    private async Task LaunchDashboardActivityAppAsync(string appId)
    {
        var service = _dashboardDataService;
        var grevId = _session.PrimaryUser?.GrevId;
        if (service is null || string.IsNullOrWhiteSpace(grevId))
        {
            _dashboardView.ShowStatus("A local Primary User is required to continue an app.");
            return;
        }

        try
        {
            var entry = await service.GetLaunchEntryAsync(grevId, appId);
            if (entry is null)
            {
                _dashboardView.ShowStatus("That app is no longer available to the current Primary User.");
                await RefreshDashboardDataAsync();
                return;
            }

            _dashboardView.ShowStatus(string.Empty);
            await LaunchInstalledAppAsync(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _dashboardView.ShowStatus($"Continue failed: {ex.Message}");
        }
    }
}
