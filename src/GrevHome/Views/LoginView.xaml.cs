using System.Windows;
using System.Windows.Controls;
using GrevHome.Sessions;

namespace GrevHome.Views;

public partial class LoginView : UserControl
{
    public event Action<AccountKind>? SignInRequested;

    public LoginView()
    {
        InitializeComponent();
    }

    private void LocalAccount_Click(object sender, RoutedEventArgs e) =>
        SignInRequested?.Invoke(AccountKind.Local);

    private void GuestAccount_Click(object sender, RoutedEventArgs e) =>
        SignInRequested?.Invoke(AccountKind.Guest);
}
