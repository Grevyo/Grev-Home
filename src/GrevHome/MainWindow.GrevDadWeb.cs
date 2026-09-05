using System.Windows;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly GrevDadWebView _grevDadWebView=new();
    private Uri? _grevDadWebTarget;
    private string? _grevDadWebOwner;
    private bool _generalWebBrowser;
    private void InitializeGrevDadWebIntegration()
    {
        _dashboardView.GrevDadRequested+=(_,_)=>OpenGrevDadWebsite(RequireGrevDadAccountService().BaseUri);
        _dashboardView.WebBrowserRequested+=(_,_)=>
        {
            if(_session.PrimaryUser?.GrevId is null) {_dashboardView.ShowStatus("Choose a local profile to open the browser.");return;}
            _generalWebBrowser=true;_grevDadWebTarget=new Uri("https://www.google.com/");_navigation.Navigate(Route.GrevDadWeb);
        };
        _grevDadWebView.ExitRequested+=(_,_)=>_navigation.GoBack();
        _navigation.RouteChanged+=route=>
        {
            if(route==Route.GrevDadWeb)
            {
                RouteHost.Content=_grevDadWebView;
                var grevId=_session.PrimaryUser?.GrevId;
                if(grevId is null) {_navigation.GoBack();return;}
                _grevDadWebOwner=grevId;
                var home=_generalWebBrowser ? new Uri("https://www.google.com/") : RequireGrevDadAccountService().BaseUri;
                var folder=_generalWebBrowser ? "WebBrowser" : "GrevDad";
                _=_grevDadWebView.OpenAsync(Path.Combine(_paths.GetProfileConnections(grevId),folder,"Browser"),home,_grevDadWebTarget??home,_generalWebBrowser);
            }
            else
            {
                _grevDadWebView.Dispose();_grevDadWebOwner=null;
            }
        };
        _session.Changed+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>
        {
            if(_navigation.Current==Route.GrevDadWeb && !string.Equals(_grevDadWebOwner,_session.PrimaryUser?.GrevId,StringComparison.OrdinalIgnoreCase))
            {_grevDadWebView.Dispose();_navigation.GoBack();}
        }));
        Closed+=(_,_)=>_grevDadWebView.Dispose();
    }

    private void OpenGrevDadWebsite(Uri target)
    {
        if(_session.PrimaryUser?.GrevId is null) {_dashboardView.ShowStatus("Choose a local profile before opening Grev.dad.");return;}
        var home=RequireGrevDadAccountService().BaseUri;
        if(target.Scheme!="https" || !string.Equals(target.Host,home.Host,StringComparison.OrdinalIgnoreCase) || target.Port!=home.Port)
        {_profileEditView.ShowGrevDadStatus("The approval address is not on the configured Grev.dad website.");return;}
        _grevDadWebTarget=target;
        _generalWebBrowser=false;
        _navigation.Navigate(Route.GrevDadWeb);
    }
}
