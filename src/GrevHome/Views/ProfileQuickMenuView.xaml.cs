using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public partial class ProfileQuickMenuView : UserControl
{
    private readonly List<Button> _actionButtons = new();
    private readonly Dictionary<string, Button> _actionButtonsByKey = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastFocusedActionKey;

    public event Action<Guid>? ViewProfileRequested;
    public event Action<Guid>? SetPrimaryRequested;
    public event Action<Guid>? SignOutPlayerRequested;
    public event Action<PlayerControllerAssignmentRequest>? AssignControllerRequested;
    public event Action<PlayerControllerAssignmentRequest>? UnassignControllerRequested;
    public event EventHandler? AddPlayerRequested;
    public event EventHandler? ManagePlayersRequested;
    public event EventHandler? CloseRequested;

    public ProfileQuickMenuView()
    {
        InitializeComponent();
        TrackPersistentFooterButton(AddPlayerButton, "footer:add-player");
        TrackPersistentFooterButton(ManagePlayersButton, "footer:manage-players");
        TrackPersistentFooterButton(CloseButton, "footer:close");
    }

    public void SetState(
        SessionContext session,
        IReadOnlyList<bool> connectedControllers,
        IReadOnlyList<LocalProfile> profiles)
    {
        var previousKey = Keyboard.FocusedElement is Button focused && focused.Tag is string key &&
                          _actionButtonsByKey.TryGetValue(key, out var tracked) && tracked == focused
            ? key
            : _lastFocusedActionKey;

        _actionButtons.Clear();
        _actionButtonsByKey.Clear();
        PlayersPanel.Children.Clear();

        var primary = session.PrimaryUser;
        SummaryText.Text = primary is null
            ? "No players are signed in."
            : session.SignedInUsers.Count == 1
                ? $"{primary.DisplayName} is signed in as the Primary User."
                : $"{session.SignedInUsers.Count} players signed in • {primary.DisplayName} is Primary.";

        AddPlayerButton.IsEnabled = primary is not null &&
                                    session.SignedInUsers.Count < 4 &&
                                    AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManagePlayers);
        ManagePlayersButton.IsEnabled = session.HasSignedInUsers;

        for (var index = 0; index < session.SignedInUsers.Count; index++)
        {
            PlayersPanel.Children.Add(CreatePlayerCard(
                index + 1,
                session.SignedInUsers[index],
                session,
                connectedControllers,
                profiles,
                primary));
        }

        RegisterFooterButton(AddPlayerButton, "footer:add-player");
        RegisterFooterButton(ManagePlayersButton, "footer:manage-players");
        RegisterFooterButton(CloseButton, "footer:close");

        if (IsVisible && !string.IsNullOrWhiteSpace(previousKey))
        {
            if (_actionButtonsByKey.TryGetValue(previousKey, out var restore) && restore.IsEnabled)
            {
                Dispatcher.BeginInvoke(new Action(() => restore.Focus()));
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(FocusInitial));
            }
        }
    }

    public void FocusInitial()
    {
        var firstPlayerAction = _actionButtons.FirstOrDefault(button =>
            button.IsVisible &&
            button.IsEnabled &&
            button.Tag is string tag &&
            !tag.StartsWith("footer:", StringComparison.OrdinalIgnoreCase));
        var first = firstPlayerAction ?? _actionButtons.FirstOrDefault(button => button.IsVisible && button.IsEnabled);
        first?.Focus();
    }

    private UIElement CreatePlayerCard(
        int playerNumber,
        SessionUser user,
        SessionContext session,
        IReadOnlyList<bool> connectedControllers,
        IReadOnlyList<LocalProfile> profiles,
        SessionUser? actor)
    {
        var profile = FindProfile(user, profiles);
        var roleBrush = GetRoleBrush(user.Role);
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = roleBrush,
            BorderThickness = new Thickness(user.IsPrimary ? 2 : 1.5),
            CornerRadius = new CornerRadius(12),
            Effect = CreateRoleEffect(user.Role, roleBrush.Color)
        };

        var root = new StackPanel();
        var identityGrid = new Grid();
        identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identityGrid.Children.Add(CreateAvatar(profile, 50, user.Role));

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(identity, 1);
        identity.Children.Add(new TextBlock
        {
            Text = user.IsPrimary ? $"PLAYER {playerNumber} • PRIMARY" : $"PLAYER {playerNumber}",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        identity.Children.Add(new TextBlock
        {
            Text = user.DisplayName,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        identity.Children.Add(new TextBlock
        {
            Text = BuildIdentityText(user),
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        identity.Children.Add(new TextBlock
        {
            Text = BuildControllerSummary(user, session, connectedControllers),
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        identityGrid.Children.Add(identity);
        root.Children.Add(identityGrid);

        var actionRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (profile is not null)
        {
            var viewButton = CreateActionButton("View Profile", $"view:{user.SessionId}");
            viewButton.Click += (_, _) => ViewProfileRequested?.Invoke(user.SessionId);
            actionRow.Children.Add(viewButton);
        }

        if (!user.IsPrimary && user.AccountKind != AccountKind.Guest)
        {
            var makePrimaryButton = CreateActionButton("Make Primary", $"primary:{user.SessionId}");
            makePrimaryButton.IsEnabled = actor is not null &&
                                          AccountAuthorizationService.Allows(actor.Role, AccountPermission.ChangePrimaryUser);
            makePrimaryButton.Click += (_, _) => SetPrimaryRequested?.Invoke(user.SessionId);
            actionRow.Children.Add(makePrimaryButton);
        }

        var canManagePlayers = actor is not null &&
                               AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers);
        var canSignOut = actor?.SessionId == user.SessionId || canManagePlayers;
        var signOutButton = CreateActionButton("Sign Out", $"signout:{user.SessionId}");
        signOutButton.IsEnabled = canSignOut;
        signOutButton.Click += (_, _) => SignOutPlayerRequested?.Invoke(user.SessionId);
        actionRow.Children.Add(signOutButton);
        root.Children.Add(actionRow);

        root.Children.Add(new TextBlock
        {
            Text = "CONTROLLER",
            Margin = new Thickness(0, 6, 0, 5),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedBrush")
        });

        var controllerRow = new WrapPanel();
        var assignedToUser = session.GetControllersForUser(user.SessionId).ToHashSet();
        var canAssignControllers = actor is not null &&
                                   AccountAuthorizationService.Allows(actor.Role, AccountPermission.AssignControllers) &&
                                   (actor.SessionId == user.SessionId || canManagePlayers);

        for (var controllerIndex = 0; controllerIndex < 4; controllerIndex++)
        {
            var connected = controllerIndex < connectedControllers.Count && connectedControllers[controllerIndex];
            var assignedUser = session.GetUserForController(controllerIndex);
            var assignedToThisUser = assignedUser?.SessionId == user.SessionId;

            if (!connected && !assignedToThisUser)
            {
                continue;
            }

            var request = new PlayerControllerAssignmentRequest(user.SessionId, controllerIndex);
            var label = assignedToThisUser
                ? connected ? $"C{controllerIndex + 1} ✓" : $"C{controllerIndex + 1} offline"
                : assignedUser is null
                    ? $"C{controllerIndex + 1}"
                    : $"C{controllerIndex + 1} • {assignedUser.DisplayName}";
            var button = CreateControllerButton(label, $"controller:{user.SessionId}:{controllerIndex}");
            button.IsEnabled = canAssignControllers;
            if (assignedToThisUser)
            {
                button.Click += (_, _) => UnassignControllerRequested?.Invoke(request);
            }
            else
            {
                button.Click += (_, _) => AssignControllerRequested?.Invoke(request);
            }
            controllerRow.Children.Add(button);
        }

        if (controllerRow.Children.Count == 0)
        {
            controllerRow.Children.Add(new TextBlock
            {
                Text = assignedToUser.Count > 0 ? "Assigned controller is disconnected." : "No connected controller available.",
                Margin = new Thickness(0, 4, 0, 2),
                FontSize = 12,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        root.Children.Add(controllerRow);
        card.Child = root;
        return card;
    }

    private Button CreateActionButton(string label, string key)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("ProfileQuickActionButtonStyle")
        };
        RegisterActionButton(button, key);
        return button;
    }

    private Button CreateControllerButton(string label, string key)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("ProfileQuickControllerButtonStyle")
        };
        RegisterActionButton(button, key);
        return button;
    }

    private void TrackPersistentFooterButton(Button button, string key)
    {
        button.Tag = key;
        button.GotKeyboardFocus += (_, _) => _lastFocusedActionKey = key;
    }

    private void RegisterFooterButton(Button button, string key)
    {
        button.Tag = key;
        _actionButtons.Add(button);
        _actionButtonsByKey[key] = button;
    }

    private void RegisterActionButton(Button button, string key)
    {
        button.Tag = key;
        button.GotKeyboardFocus += (_, _) => _lastFocusedActionKey = key;
        _actionButtons.Add(button);
        _actionButtonsByKey[key] = button;
    }

    private Border CreateAvatar(LocalProfile? profile, double size, AccountRole role)
    {
        var imageSource = profile is null ? null : ProfileAvatarCatalog.TryLoadCustomImage(profile);
        var grid = new Grid();
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
                Text = profile is null ? "?" : ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return new Border
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, 12, 0),
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            BorderBrush = GetRoleBrush(role),
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            Child = grid
        };
    }

    private static string BuildIdentityText(SessionUser user) =>
        user.AccountKind == AccountKind.Guest
            ? "Temporary Guest • Guest role"
            : string.IsNullOrWhiteSpace(user.Username)
                ? user.Role.ToString()
                : $"@{user.Username} • {user.Role}";

    private static string BuildControllerSummary(
        SessionUser user,
        SessionContext session,
        IReadOnlyList<bool> connectedControllers)
    {
        var controllers = session.GetControllersForUser(user.SessionId);
        if (controllers.Count == 0)
        {
            return "No controller assigned";
        }

        return string.Join(" • ", controllers.Select(index =>
        {
            var connected = index >= 0 && index < connectedControllers.Count && connectedControllers[index];
            return connected ? $"Controller {index + 1}" : $"Controller {index + 1} disconnected";
        }));
    }

    private SolidColorBrush GetRoleBrush(AccountRole role) =>
        (SolidColorBrush)FindResource(role switch
        {
            AccountRole.Admin => "AdminRoleBrush",
            AccountRole.Standard => "StandardRoleBrush",
            _ => "GuestRoleBrush"
        });

    private static DropShadowEffect? CreateRoleEffect(AccountRole role, Color color) => role switch
    {
        AccountRole.Admin => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 12,
            ShadowDepth = 0,
            Opacity = 0.35
        },
        AccountRole.Standard => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 5,
            ShadowDepth = 0,
            Opacity = 0.12
        },
        _ => null
    };

    private static LocalProfile? FindProfile(SessionUser? user, IReadOnlyList<LocalProfile> profiles)
    {
        if (user?.GrevId is null) return null;
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.GrevId, user.GrevId, StringComparison.OrdinalIgnoreCase));
    }

    private void AddPlayer_Click(object sender, RoutedEventArgs e) =>
        AddPlayerRequested?.Invoke(this, EventArgs.Empty);

    private void ManagePlayers_Click(object sender, RoutedEventArgs e) =>
        ManagePlayersRequested?.Invoke(this, EventArgs.Empty);

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
