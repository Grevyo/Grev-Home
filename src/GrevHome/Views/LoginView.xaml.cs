using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record ProfileSignInRequest(LocalProfile Profile, int? ControllerIndex);

public partial class LoginView : UserControl
{
    public event Action<ProfileSignInRequest>? LocalProfileSignInRequested;

    // Kept for compatibility with the current shell wiring while the old lobby surface is removed.
    // The current Login UI does not expose a pre-made Guest account or primary-selection lobby.
    public event Action<int?>? GuestSignInRequested;
    public event Action<Guid>? PrimaryUserRequested;
    public event EventHandler? CreateProfileRequested;
    public event EventHandler? EnterHomeRequested;
    public event EventHandler? ClearSessionRequested;

    public int? ActivationControllerIndex { get; set; }
    public Button CreateAccountFocusTarget => CreateAccountButton;
    public IReadOnlyList<Button> ProfileFocusTargets => ProfilesPanel.Children
        .OfType<Button>()
        .Where(button => button.IsVisible && button.IsEnabled && button.Focusable)
        .ToArray();

    public LoginView()
    {
        InitializeComponent();
    }

    public void Refresh(
        IReadOnlyList<LocalProfile> profiles,
        SessionContext session,
        IReadOnlyList<bool> connectedControllers)
    {
        HeadingText.Text = session.HasSignedInUsers ? "Players" : "Who's playing?";
        SubheadingText.Text = session.HasSignedInUsers
            ? "Choose another profile to sign them in, or log out of the current Grev Home session."
            : "Choose your profile to enter Grev Home.";
        LogoutButton.Visibility = session.HasSignedInUsers
            ? Visibility.Visible
            : Visibility.Collapsed;

        ProfilesPanel.Children.Clear();
        foreach (var profile in profiles)
        {
            var signedIn = session.SignedInUsers.FirstOrDefault(user =>
                string.Equals(user.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));

            var status = signedIn is null
                ? profile.Role.ToString()
                : BuildSignedInLabel(session, signedIn);

            var button = new Button
            {
                Width = 260,
                Height = 154,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = profile,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = profile.DisplayName,
                            FontSize = 23,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = $"@{profile.Username}",
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 13,
                            MaxWidth = 220,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = profile.Role.ToString().ToUpperInvariant(),
                            Margin = new Thickness(0, 5, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold
                        },
                        new TextBlock
                        {
                            Text = status,
                            Margin = new Thickness(0, 7, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 12,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            button.Click += LocalProfile_Click;
            ProfilesPanel.Children.Add(button);
        }

        NoProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildSignedInLabel(SessionContext session, SessionUser user)
    {
        var controllers = session.GetControllersForUser(user.SessionId);
        var controllerText = controllers.Count == 0
            ? "No controller"
            : string.Join(", ", controllers.Select(index => $"Controller {index + 1}"));
        return $"SIGNED IN • {controllerText}{(user.IsPrimary ? " • PRIMARY" : string.Empty)}";
    }

    private void LocalProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalProfile profile })
        {
            return;
        }

        LocalProfileSignInRequested?.Invoke(new ProfileSignInRequest(profile, ActivationControllerIndex));
        EnterHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CreateProfile_Click(object sender, RoutedEventArgs e) =>
        CreateProfileRequested?.Invoke(this, EventArgs.Empty);

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        ClearSessionRequested?.Invoke(this, EventArgs.Empty);
}
