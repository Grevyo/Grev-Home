using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record ProfileSignInRequest(LocalProfile Profile, int? ControllerIndex);

public partial class LoginView : UserControl
{
    public event Action<ProfileSignInRequest>? LocalProfileSignInRequested;

    // Legacy shell events remain declared only until the MainWindow wiring is normalized.
    // No current Login control raises these and there is no pre-made Guest UI.
    public event Action<int?>? GuestSignInRequested;
    public event Action<Guid>? PrimaryUserRequested;
    public event EventHandler? ClearSessionRequested;

    public event EventHandler? CreateProfileRequested;
    public event EventHandler? EnterHomeRequested;

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
        var addingPlayer = session.HasSignedInUsers;
        HeadingText.Text = addingPlayer
            ? $"Player {session.SignedInUsers.Count + 1} Sign In"
            : "Who's playing?";
        SubheadingText.Text = addingPlayer
            ? "Choose a profile that is not already signed in. The controller used to select it will be assigned to that player."
            : "Choose your profile to enter Grev Home.";

        var canCreateAccount = profiles.Count == 0 ||
                               session.PrimaryUser is { } primary &&
                               AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManageProfiles);
        CreateAccountButton.Visibility = canCreateAccount ? Visibility.Visible : Visibility.Collapsed;

        ProfilesPanel.Children.Clear();
        foreach (var profile in profiles)
        {
            var signedIn = session.SignedInUsers.FirstOrDefault(user =>
                string.Equals(user.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));

            var avatar = new Border
            {
                Width = 54,
                Height = 54,
                CornerRadius = new CornerRadius(27),
                Margin = new Thickness(0, 0, 0, 7),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 40, 58)),
                Child = new TextBlock
                {
                    Text = ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName),
                    FontSize = 21,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var button = new Button
            {
                Width = 260,
                Height = 176,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = profile,
                IsEnabled = !addingPlayer || signedIn is null,
                Content = new StackPanel
                {
                    Children =
                    {
                        avatar,
                        new TextBlock
                        {
                            Text = profile.DisplayName,
                            FontSize = 21,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 220,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = $"@{profile.Username}  •  {profile.Role}",
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 12,
                            MaxWidth = 220,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = signedIn is null ? "A / Enter to sign in" : BuildSignedInLabel(session, signedIn),
                            Margin = new Thickness(0, 6, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 11,
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
}
