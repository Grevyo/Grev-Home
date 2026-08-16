using GrevHome.Navigation;

namespace GrevHome;

public partial class MainWindow
{
    private bool _guestSessionIntegrationReady;

    private void InitializeGuestSessionIntegration()
    {
        if (_guestSessionIntegrationReady)
        {
            return;
        }

        _guestSessionIntegrationReady = true;
        _loginView.GuestSignInRequested += CompleteTemporaryGuestJoin;
    }

    private void CompleteTemporaryGuestJoin(int? controllerIndex)
    {
        if (_navigation.Current != Route.Login || !_session.HasSignedInUsers)
        {
            return;
        }

        _loginView.ClearStatus();
        CloseSessionLobby();
    }
}
