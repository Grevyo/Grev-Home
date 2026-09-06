using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record ProfileSignInRequest(LocalProfile Profile, int? ControllerIndex);

public partial class LoginView : UserControl
{
    private SessionContext? _session;
    private Button? _lastProfileFocus;
    private ProfileSignInRequest? _pendingPasswordProfile;
    private IReadOnlyDictionary<string, ProfileStatsSnapshot> _stats = new Dictionary<string, ProfileStatsSnapshot>();
    private IReadOnlyDictionary<string, ProfilePresentationSettings> _presentations = new Dictionary<string, ProfilePresentationSettings>();

    public event Action<ProfileSignInRequest>? LocalProfileSignInRequested;
    public event Action<int?>? GuestSignInRequested;
    public event EventHandler? CreateProfileRequested;
    public event Action<ProfileSignInRequest, string>? PasswordSignInRequested;

    public int? ActivationControllerIndex { get; set; }
    public Button CreateAccountFocusTarget => CreateAccountButton;
    public IReadOnlyList<Button> ProfileFocusTargets => ProfilesPanel.Children.OfType<Button>().Where(button => button.IsVisible && button.IsEnabled && button.Focusable).ToArray();

    public LoginView()
    {
        InitializeComponent();
        PasswordKeyboard.Completed += password =>
        {
            if (_pendingPasswordProfile is { } request) PasswordSignInRequested?.Invoke(request, password);
            _pendingPasswordProfile = null;
        };
        PasswordKeyboard.Cancelled += (_, _) => _pendingPasswordProfile = null;
    }

    public void SetProfileDetails(IReadOnlyDictionary<string, ProfileStatsSnapshot> stats,
        IReadOnlyDictionary<string, ProfilePresentationSettings> presentations)
    { _stats = stats; _presentations = presentations; if (_session is not null) Refresh(_lastProfiles, _session, _lastControllers); }

    private IReadOnlyList<LocalProfile> _lastProfiles = Array.Empty<LocalProfile>();
    private IReadOnlyList<bool> _lastControllers = Array.Empty<bool>();

    public void Refresh(IReadOnlyList<LocalProfile> profiles, SessionContext session, IReadOnlyList<bool> connectedControllers)
    {
        _lastProfiles = profiles; _lastControllers = connectedControllers;
        _session = session;
        var addingPlayer = session.HasSignedInUsers;
        var slotsFull = session.SignedInUsers.Count >= SessionContext.MaximumPlayers;
        var canAddPlayers = !addingPlayer ||
                            session.PrimaryUser is { } primaryForPlayers &&
                            AccountAuthorizationService.Allows(primaryForPlayers.Role, AccountPermission.ManagePlayers);

        HeadingText.Text = addingPlayer
            ? slotsFull ? "All player slots are in use" : $"Player {session.SignedInUsers.Count + 1} Sign In"
            : "Who's playing?";
        SubheadingText.Text = addingPlayer
            ? slotsFull
                ? $"{SessionContext.MaximumPlayers} players are already signed in. Go back to Who's Playing or Manage Players to change the current session."
                : canAddPlayers
                    ? "Choose another local profile or Temporary Guest. Use an unassigned controller to join, or use keyboard/mouse to join without a controller."
                    : "The current Primary User is not allowed to add another player. Press B / Esc to return."
            : "Choose a profile.";
        BackHintText.Visibility = addingPlayer ? Visibility.Visible : Visibility.Collapsed;

        var canCreateAccount = !addingPlayer ||
                               session.PrimaryUser is { } primary &&
                               AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManageProfiles);
        CreateAccountButton.Visibility = canCreateAccount && !slotsFull ? Visibility.Visible : Visibility.Collapsed;

        ProfilesPanel.Children.Clear();
        foreach (var profile in profiles)
        {
            _stats.TryGetValue(profile.GrevId, out var stats);
            _presentations.TryGetValue(profile.GrevId, out var presentation);
            var signedIn = session.SignedInUsers.FirstOrDefault(user => string.Equals(user.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));
            var button = new Button
            {
                Width = 300,
                Height = 380,
                Margin = new Thickness(10, 0, 10, 0),
                Padding = new Thickness(18),
                Background = CreateCardBackground(profile, presentation),
                Tag = profile,
                IsEnabled = !slotsFull && (!addingPlayer || canAddPlayers && signedIn is null),
                Content = new StackPanel
                {
                    Children =
                    {
                        CreateAvatar(profile),
                        new TextBlock { Text = profile.DisplayName, FontSize = 27, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 245, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $"@{profile.Username}  •  {profile.Role}", Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = BuildProfileSummary(profile, stats), Margin = new Thickness(0,12,0,6), MaxWidth = 245, FontSize = 14, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = signedIn is null ? profile.HasControllerPassword ? "PASSWORD PROTECTED" : string.Empty : BuildSignedInLabel(session, signedIn), Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
                    }
                }
            };
            button.Click += LocalProfile_Click;
            button.GotKeyboardFocus += Profile_GotFocus;
            ProfilesPanel.Children.Add(button);
        }

        if (addingPlayer && !slotsFull && canAddPlayers)
        {
            ProfilesPanel.Children.Add(CreateTemporaryGuestButton());
        }

        NoProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResizeCards();
        ProfilesScroll.ScrollToHorizontalOffset(0);
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
            Width = 300,
            Height = 380,
            Margin = new Thickness(10, 0, 10, 0),
            Background = ProfileBannerCatalog.CreateBrush("mono"),
            Content = new StackPanel
            {
                Children =
                {
                    CreateTemporaryGuestAvatar(),
                    new TextBlock { Text = "Temporary Guest", FontSize = 21, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "No GrevID • shared guest data", Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 },
                    new TextBlock { Text = "Borrowing the sofa. Returning nothing.", Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }
                }
            }
        };
        button.Click += TemporaryGuest_Click;
        button.GotKeyboardFocus += Profile_GotFocus;
        return button;
    }

    private Border CreateTemporaryGuestAvatar()
    {
        const double size = 110;
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
                FontSize = 42,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Border CreateAvatar(LocalProfile profile)
    {
        const double size = 110;
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
                FontSize = 42,
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
            Margin = new Thickness(0, 0, 0, 18),
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

        var session = _session;
        if (session?.HasSignedInUsers == true &&
            (session.PrimaryUser is not { } primary || !AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManagePlayers)))
        {
            ShowStatus("The current Primary User is not allowed to add another player.");
            return;
        }

        var request = new ProfileSignInRequest(profile, ActivationControllerIndex);
        if (profile.HasControllerPassword)
        {
            _pendingPasswordProfile = request;
            PasswordKeyboard.Open($"Password for {profile.DisplayName}", string.Empty, 64, password: true);
            return;
        }
        LocalProfileSignInRequested?.Invoke(request);
    }

    private static string BuildProfileSummary(LocalProfile profile, ProfileStatsSnapshot? stats)
    {
        if (profile.IsBuiltInGuest) return "Guest pass • no membership required\nSnacks and questionable choices welcome";
        if (stats is null) return string.IsNullOrWhiteSpace(profile.StatusMessage) ? $"Member since {profile.CreatedAtUtc.ToLocalTime():yyyy}" : profile.StatusMessage;
        var hours = TimeSpan.FromSeconds(stats.TotalTrackedSeconds).TotalHours;
        var activity = $"Level {stats.Progression.Level}  •  {stats.Progression.TotalXp:N0} XP\n{hours:0.#} hours  •  {stats.CompletedSessions:N0} sessions";
        return string.IsNullOrWhiteSpace(profile.StatusMessage) ? activity : profile.StatusMessage + "\n" + activity;
    }

    private static Brush CreateCardBackground(LocalProfile profile, ProfilePresentationSettings? presentation)
    {
        presentation ??= ProfilePresentationSettings.Default;
        var image = ProfileBannerCatalog.TryLoadCustomImage(profile.GrevId, presentation);
        return image is null ? ProfileBannerCatalog.CreateBrush(presentation.BannerKey) : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    }

    private void TemporaryGuest_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null || !session.HasSignedInUsers || session.SignedInUsers.Count >= SessionContext.MaximumPlayers)
        {
            return;
        }

        if (session.PrimaryUser is not { } primary || !AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManagePlayers))
        {
            ShowStatus("The current Primary User is not allowed to add a temporary Guest.");
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

    public bool MoveProfileFocus(GrevHome.Input.InputAction action, Button original)
    {
        var cards = ProfileFocusTargets.ToList();
        var index = cards.IndexOf(original);
        if (index >= 0 && action is GrevHome.Input.InputAction.Left or GrevHome.Input.InputAction.Right)
        {
            cards[Math.Clamp(index+(action==GrevHome.Input.InputAction.Right?1:-1),0,cards.Count-1)].Focus();
            return true;
        }
        if (index >= 0 && action == GrevHome.Input.InputAction.Down)
        {
            if (CreateAccountButton.IsVisible && CreateAccountButton.IsEnabled) CreateAccountButton.Focus();
            else original.Focus();
            return true;
        }
        if (original == CreateAccountButton && action == GrevHome.Input.InputAction.Up && cards.Count>0)
        {
            (cards.Contains(_lastProfileFocus!) ? _lastProfileFocus! : cards[0]).Focus();
            return true;
        }
        return false;
    }

    private void Profile_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Button button) return;
        _lastProfileFocus = button;
        Dispatcher.BeginInvoke(new Action(()=>
        {
            if (!button.IsKeyboardFocusWithin || !ProfilesPanel.Children.Contains(button)) return;
            var left=button.TranslatePoint(new Point(0,0),ProfilesPanel).X;
            var offset=ProfilesScroll.HorizontalOffset;
            if(left<offset+24) ProfilesScroll.ScrollToHorizontalOffset(Math.Max(0,left-24));
            else if(left+button.ActualWidth>offset+ProfilesScroll.ViewportWidth-64)
                ProfilesScroll.ScrollToHorizontalOffset(left+button.ActualWidth-ProfilesScroll.ViewportWidth+64);
            CarouselHintText.Text = $"Profile {ProfilesPanel.Children.IndexOf(button)+1} of {ProfilesPanel.Children.Count}";
        }));
    }

    private void ResizeCards()
    {
        if (CarouselHost.ActualWidth<=0) return;
        var width=Math.Max(150,Math.Min(340,(CarouselHost.ActualWidth-56)/4-20));
        foreach(var button in ProfilesPanel.Children.OfType<Button>())
        {
            button.Width=width;
            button.Height=Math.Clamp(CarouselHost.ActualHeight-28,280,410);
            if(button.Content is StackPanel content)
                foreach(var text in content.Children.OfType<TextBlock>()) text.MaxWidth=Math.Max(110,width-40);
        }
        CarouselHintText.Text = ProfilesPanel.Children.Count>4 ? "More profiles are available" : string.Empty;
    }
    private void Carousel_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeCards();
    private void Profiles_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ProfilesScroll.ScrollToHorizontalOffset(ProfilesScroll.HorizontalOffset-e.Delta*2);
        e.Handled=true;
    }
    private void Profiles_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var left=ProfilesScroll.HorizontalOffset>1;
        var right=ProfilesScroll.HorizontalOffset<ProfilesScroll.ScrollableWidth-1;
        ProfilesScroll.OpacityMask=new LinearGradientBrush(new GradientStopCollection {
            new(left?Colors.Transparent:Colors.Black,0),new(Colors.Black,0.035),
            new(Colors.Black,0.965),new(right?Colors.Transparent:Colors.Black,1)
        },new Point(0,0),new Point(1,0));
    }
}
