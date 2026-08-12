namespace GrevHome.Views;

public partial class GrevOverlayWindow
{
    // The old MainWindow subscription still exists on the stacked draft branch, but App Killer no
    // longer raises it. Referencing the event here keeps that temporary compatibility hook from
    // producing CS0067 while the active-app path is owned entirely by OverlayMode.AppKiller.
    // Remove this helper together with the old MainWindow subscription during the constructor
    // cleanup pass; do not call this method from the active-app overlay.
    private void MarkLegacyAppKillerHookAsReferenced() =>
        _ = AppKillerRequested;
}
