namespace GrevHome;

public partial class MainWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        // Online identity is a shell foundation, not a page-owned feature. Initialize it from the
        // native MainWindow lifecycle so Friends/Profile/Activity surfaces can remain consumers.
        InitializeGrevDadIntegration();
        InitializeGrevDadSettingsIntegration();
    }
}
