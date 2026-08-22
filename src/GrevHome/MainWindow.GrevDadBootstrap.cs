namespace GrevHome;

public partial class MainWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        // Diagnostics are local-only and non-blocking. Capture a machine-health baseline at startup
        // before the later whole-appliance test needs to correlate failures with on-disk state.
        InitializeMachineHealthIntegration();

        // Completed-session history is always local-first and exists whether Grev.dad is linked or not.
        InitializeSessionHistoryIntegration();

        // Online identity is a shell foundation, not a page-owned feature. Initialize it from the
        // native MainWindow lifecycle so later Friends/Profile/Activity surfaces remain consumers.
        InitializeGrevDadIntegration();
        InitializeGrevDadMaintenanceIntegration();
        InitializeGrevDadProfileSyncIntegration();
        InitializeGrevDadSettingsIntegration();
    }
}
