using GrevHome.Navigation;

namespace GrevHome;

public partial class MainWindow
{
    private void SignInTemporaryGuest(int? controllerIndex)
    {
        if (_navigation.Current != Route.Login || !_session.HasSignedInUsers)
        {
            return;
        }

        try
        {
            _session.SignInGuest(controllerIndex);
            _loginView.ClearStatus();
            CloseSessionLobby();
        }
        catch (InvalidOperationException ex)
        {
            _loginView.ShowStatus(ex.Message);
        }
    }
}
