using System.Windows.Threading;
using GrevHome.Online;

namespace GrevHome;

public partial class MainWindow
{
    private readonly DispatcherTimer _grevDadMaintenanceTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };
    private GrevDadConnectionMaintenanceService? _grevDadMaintenance;
    private bool _grevDadMaintenanceReady;
    private int _grevDadMaintenanceActive;

    private void InitializeGrevDadMaintenanceIntegration()
    {
        if (_grevDadMaintenanceReady)
        {
            return;
        }

        _grevDadMaintenanceReady = true;
        _grevDadMaintenance = new GrevDadConnectionMaintenanceService(
            _paths,
            RequireGrevDadAccountService());

        _grevDadMaintenanceTimer.Tick += (_, _) => _ = MaintainAllGrevDadLinksSafeAsync();
        _grevDadMaintenanceTimer.Start();

        // Read profiles independently rather than assuming Loaded/profile enumeration has already
        // finished. Online maintenance must not introduce an ordering dependency into shell startup.
        Dispatcher.BeginInvoke(new Action(() => _ = MaintainAllGrevDadLinksSafeAsync()), DispatcherPriority.Background);

        Closed += (_, _) =>
        {
            _grevDadMaintenanceTimer.Stop();
            _grevDadMaintenance?.Dispose();
        };
    }

    private async Task MaintainAllGrevDadLinksSafeAsync()
    {
        var maintenance = _grevDadMaintenance;
        if (maintenance is null || Interlocked.Exchange(ref _grevDadMaintenanceActive, 1) != 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<Profiles.LocalProfile> profiles;
            try
            {
                profiles = await _profileService.GetProfilesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return;
            }

            foreach (var profile in profiles)
            {
                try
                {
                    await maintenance.MaintainLinkedProfileAsync(profile);
                }
                catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                           ex is IOException or UnauthorizedAccessException)
                {
                    // Capability refresh/rotation is maintenance only. A failure cannot affect the
                    // local profile, sign-in, app runtime or any other linked GrevID on the machine.
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _grevDadMaintenanceActive, 0);
        }
    }
}
