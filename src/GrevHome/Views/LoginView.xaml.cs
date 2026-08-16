using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record ProfileSignInRequest(LocalProfile Profile, int? ControllerIndex);

public partial class LoginView : UserControl
{
    private SessionContext? _session;

    public event Action<ProfileSignInRequest>? LocalProfileSignInRequested;
    public event Action<int?>? GuestSignInRequested;

    public event Action<Guid>? PrimaryUserRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? ClearSessionRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? CreateProfileRequested;

    public int? ActivationControllerIndex { get; set; }
    public Button CreateAccountFocusTarget => CreateAccountButton;
    public IReadOnlyList<Button> ProfileFocusTargets => ProfilesPanel.Children.OfType<Button>().Where(button => button.IsVisible && button.IsEnabled && button.Focusable).ToArray();

    public LoginView()
    {
        InitializeComponent();
    }

    public void Refresh(IReadOnlyList<LocalProfile> profiles, SessionContext session, IReadOnlyList<bool> connectedControllers)
    {
        _session = session;
        var addingPlayer = session.HasSignedInUsers;
        var slotsFull = session.SignedInUsers.Count >= 4;
        HeadingText.Text = addingPlayer
            ? slotsFull ? "All player slots are in use" : $"Player {session.SignedInUsers.Count + 1} Sign In"
            : "Who's playing?";
        SubheadingText.Text = addingPlayer
            ? slotsFull
                ? "Four players are already signed in. Go back to Who's Playing or Manage Players to change the current session."
                : "Choose another local profile or Temporary Guest. Use an unassigned controller to join, or use keyboard/mouse to join without a controller."
            : "Choose your profile to enter Grev Home.";
        BackHintText.Visibility = addingPlayer ? Visibility.Visible : Visibility.Collapsed;

        var canCreateAccount = profiles.Count == 0 || session.PrimaryUser is { } primary && AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManageProfiles);
        CreateAccountButton.Visibility = canCreateAccount && !slotsFull ? Visibility.Visible : Visibility.Collapsed;

        ProfilesPanel.Children.Clear();
        foreach (var profile in profiles)
        {
            var signedIn = session.SignedInUsers.FirstOrDefault(user => string.Equals(user.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));
            var button = new Button
            {
                Width = 260,
                Height = 176,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = profile,
                IsEnabled = !slotsFull && (!addingPlayer || signedIn is null),
                Content = new StackPanel
                {
                    Children =
                    {
                        CreateAvatar(profile),
                        new TextBlock { Text = profile.DisplayName, FontSize = 21, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $"@{profile.Username}  •  {profile.Role}", Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = signedIn is null ? slotsFull ? "SESSION FULL" : addingPlayer ? "A / Enter to join" : "A / Enter to sign in" : BuildSignedInLabel(session, signedIn), Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
                    }
                }
            };
            button.Click += LocalProfile_Click;
            ProfilesPanel.Children.Add(button);
        }

        if (addingPlayer && !slotsFull)
        {
            ProfilesPanel.Children.Add(CreateTemporaryGuestButton());
        }

        NoProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ClearStatus() => ShowStatus(string.Empty);

    private Button CreateTemporaryGuestButton()
    {
        var button = new Button
        {
            Width = 260,
            Height = 176,
            Margin = new Thickness(0, 0, 10, 10),
            Content = new StackPanel
            {
                Children =
                {
                    CreateTemporaryGuestAvatar(),
                    new TextBlock { Text = "Temporary Guest", FontSize = 21, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "No GrevID • shared guest data", Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 },
                    new TextBlock { Text = "A / Enter to join", Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 }
                }
            }
        };
        button.Click += TemporaryGuest_Click;
        return button;
    }

    private Border CreateTemporaryGuestAvatar()
    {
        const double size = 54;
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Margin = new Thickness(0, 0, 0, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            BorderBrush = (Brush)FindResource("GuestRoleBrush"),
            BorderThickness = new Thickness(1.5),
            Child = new TextBlock
            {
                Text = "?",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Border CreateAvatar(LocalProfile profile)
    {
        const double size = 54;
        var grid = new Grid();
        var imageSource = ProfileAvatarCatalog.TryLoadCustomImage(profile);
        if (imageSource is not null)
        {
            grid.Children.Add(new Image
            {
                Source = imageSource,
                Stretch = Stretch.UniformToFill,
                Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2)
            });
        }
        else
        {
            grid.Children.Add(new TextBlock
            {
                Text = ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName),
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var roleBrush = (Brush)FindResource(profile.Role switch
        {
            AccountRole.Admin => "AdminRoleBrush",
            AccountRole.Standard => "StandardRoleBrush",
            _ => "GuestRoleBrush"
        });

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Margin = new Thickness(0, 0, 0, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            BorderBrush = roleBrush,
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            Child = grid
        };
    }

    private static string BuildSignedInLabel(SessionContext session, SessionUser user)
    {
        var controllers = session.GetControllersForUser(user.SessionId);
        var controllerText = controllers.Count == 0 ? "No controller" : string.Join(", ", controllers.Select(index => $"Controller {index + 1}"));
        return $"SIGNED IN • {controllerText}{(user.IsPrimary ? " • PRIMARY" : string.Empty)}";
    }

    private void LocalProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalProfile profile }) return;
        LocalProfileSignInRequested?.Invoke(new ProfileSignInRequest(profile, ActivationControllerIndex));
    }

    private void TemporaryGuest_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null || !session.HasSignedInUsers || session.SignedInUsers.Count >= 4)
        {
            return;
        }

        if (ActivationControllerIndex is int controllerIndex && session.GetUserForController(controllerIndex) is { } currentOwner)
        {
            ShowStatus($"Controller {controllerIndex + 1} is already assigned to {currentOwner.DisplayName}. Use an unassigned controller to join a Guest.");
            return;
        }

        ClearStatus();
        GuestSignInRequested?.Invoke(ActivationControllerIndex);
    }

    private void CreateProfile_Click(object sender, RoutedEventArgs e) => CreateProfileRequested?.Invoke(this, EventArgs.Empty);
}
