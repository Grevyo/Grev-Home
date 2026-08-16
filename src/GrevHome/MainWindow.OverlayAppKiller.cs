namespace GrevHome;

/// <summary>
/// Keeps App Killer actions invoked from the external-app overlay inside that runtime context.
/// MainWindow remains hidden, so opening App Killer does not disable the active app's controller
/// mode merely to expose management controls.
/// </summary>
public partial class MainWindow
{
    private bool _overlayAppKillerIntegrationReady;

    private void InitializeOverlayAppKillerIntegration()
    {
        if (_overlayAppKillerIntegrationReady)
        {
            return;
        }

        _overlayAppKillerIntegrationReady = true;

        _overlayWindow.AppKillerCloseRequested += launchSessionId =>
        {
            RequestCloseSession(launchSessionId);
            ScheduleManagedCloseEscalation(launchSessionId);
        };

        _overlayWindow.AppKillerForceCloseRequested += ForceCloseSession;
    }
}
