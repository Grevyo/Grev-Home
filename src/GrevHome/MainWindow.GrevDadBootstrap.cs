namespace GrevHome;

public partial class MainWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        // WPF can raise Initialized while InitializeComponent is still unwinding, before the
        // MainWindow constructor has assigned services such as _runtimeSessions. Defer all
        // backbone integration wiring until Loaded, when constructor-owned services are ready and
        // the native window is being shown. Every integration has its own one-time guard.
        Loaded += (_, _) => InitializeBackboneIntegrations();
    }

    private void InitializeBackboneIntegrations()
    {
        // Diagnostics are local-only and non-blocking. Capture a machine-health baseline at startup
        // before the later whole-appliance test needs to correlate failures with on-disk state.
        InitializeMachineHealthIntegration();

        // Completed-session history is always local-first and exists whether Grev.dad is linked or not.
        InitializeSessionHistoryIntegration();

        // Online identity is a shell foundation, not a page-owned feature. Initialize it only after
        // MainWindow construction has completed so these consumers cannot observe half-built services.
        InitializeGrevDadIntegration();
        InitializeGrevDadMaintenanceIntegration();
        InitializeGrevDadProfileSyncIntegration();
        InitializeGrevDadSettingsIntegration();
    }
}
