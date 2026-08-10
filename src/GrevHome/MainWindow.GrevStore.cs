using GrevHome.Navigation;
using GrevHome.Store;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly GrevStoreView _grevStoreView = new();
    private readonly GrevStoreCatalogService _grevStoreCatalog = new();
    private readonly AppPresentationService _appPresentationService;
    private bool _grevStoreIntegrationReady;

    private void InitializeGrevStoreIntegration()
    {
        if (_grevStoreIntegrationReady) return;
        _grevStoreIntegrationReady = true;

        _appPresentationService = new AppPresentationService(_paths);
        _dashboardView.StoreRequested += (_, _) => OpenGrevStore();
        _grevStoreView.PackageRequested += OpenStorePackage;
        _navigation.RouteChanged += HandleGrevStoreRouteChanged;
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.GrevStore)
            {
                Dispatcher.BeginInvoke(new Action(RefreshGrevStore));
            }
        };
    }

    private void OpenGrevStore()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        RefreshGrevStore();
        _navigation.Navigate(Route.GrevStore);
    }

    private void RefreshGrevStore() =>
        _grevStoreView.SetStore(_grevStoreCatalog.GetAll(), _session.PrimaryUser);

    private void HandleGrevStoreRouteChanged(Route route)
    {
        if (route != Route.GrevStore) return;
        RefreshGrevStore();
        RouteHost.Content = _grevStoreView;
        FocusRouteSoon();
    }

    private void OpenStorePackage(GrevStorePackageDefinition package)
    {
        if (package.IsProfileInstall && string.IsNullOrWhiteSpace(_session.PrimaryUser?.GrevId))
        {
            _grevStoreView.ShowStatus("A persistent local Primary User is required to install this Profile App.");
            return;
        }

        _grevStoreView.ShowStatus(
            $"{package.Presentation.DisplayName} is registered as a trusted {package.Category} package. " +
            $"Installer '{package.InstallerId}' will install it as {(package.IsProfileInstall ? "a Profile App for the current Primary GrevID" : "a Global App")}. " +
            "The package-specific download/install workflow is the next 0.11 step.");
    }
}
