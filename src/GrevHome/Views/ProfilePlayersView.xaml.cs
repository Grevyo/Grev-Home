using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record PlayerControllerAssignmentRequest(Guid SessionUserId, int ControllerIndex);

public partial class ProfilePlayersView : UserControl
{
    private Guid? _primarySessionUserId;

    public event Action<Guid>? ViewProfileRequested;
    public event Action<Guid>? EditProfileRequested;
    public event EventHandler? AddPlayerRequested;
    public event EventHandler? LogoutRequested;
    public event Action<Guid>? SignOutPlayerRequested;
    public event Action<Guid>? SetPrimaryRequested;
    public event Action<PlayerControllerAssignmentRequest>? AssignControllerRequested;
    public event Action<PlayerControllerAssignmentRequest>? UnassignControllerRequested;

    public ProfilePlayersView()
    {
        InitializeComponent();
    }

    public void SetState(SessionContext session, IReadOnlyList<bool> connectedControllers, IReadOnlyList<LocalProfile> profiles)
    {
        var primary = session.PrimaryUser;
        _primarySessionUserId = primary?.SessionId;
        var primaryProfile = FindProfile(primary, profiles);
        ApplyPrimaryAvatar(primaryProfile);
        ApplyPrimaryRole(primary?.Role ?? AccountRole.Guest);

        PrimaryNameText.Text = primary?.DisplayName ?? "No primary profile";
        PrimaryIdentityText.Text = primary is null ? "No user is signed in." : $"@{primary.Username}  •  {primary.Role}  •  Primary User";
        SummaryText.Text = session.SignedInUsers.Count == 1
            ? "1 player signed in. Add Player 2 or manage the current profile and controller assignment."
            : $"{session.SignedInUsers.Count} players signed in. Manage profiles, Primary User and controller assignments here.";

        var canManagePlayers = primary is not null && AccountAuthorizationService.Allows(primary.Role, AccountPermission.ManagePlayers);
        AddPlayerButton.Content = $"Player {session.SignedInUsers.Count + 1} Sign In";
        AddPlayerButton.IsEnabled = session.SignedInUsers.Count < 4 && canManagePlayers;

        var canEditPrimary = primary?.GrevId is not null && AccountAuthorizationService.CanEditProfile(primary.Role, primary.GrevId, primary.GrevId);
        ViewPrimaryButton.IsEnabled = primaryProfile is not null;
        EditPrimaryButton.IsEnabled = primaryProfile is not null && canEditPrimary;

        PlayersPanel.Children.Clear();
        for (var index = 0; index < session.SignedInUsers.Count; index++)
        {
            PlayersPanel.Children.Add(CreatePlayerCard(index + 1, session.SignedInUsers[index], session, connectedControllers, profiles, primary));
        }

        StatusText.Text = connectedControllers.Any(isConnected => isConnected)
            ? "Assigned controllers remain owned by their player if they disconnect. Reconnecting the same controller slot restores it automatically; select its C button to unassign it deliberately."
            : "No XInput controllers are currently connected. Existing assignments remain visible and players stay signed in; reconnect the same controller slot or unassign it deliberately.";
    }

    private UIElement CreatePlayerCard(int playerNumber, SessionUser user, SessionContext session, IReadOnlyList<bool> connectedControllers, IReadOnlyList<LocalProfile> profiles, SessionUser? actor)
    {
        var profile = FindProfile(user, profiles);
        var roleBrush = GetRoleBrush(user.Role);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = roleBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12),
            Effect = CreateRoleEffect(user.Role, roleBrush.Color)
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(CreateAvatar(profile, 66, user.Role));

        var details = new StackPanel();
        Grid.SetColumn(details, 1);
        details.Children.Add(new TextBlock { Text = $"PLAYER {playerNumber}", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush") });
        details.Children.Add(new TextBlock { Text = user.DisplayName, Margin = new Thickness(0, 5, 0, 0), FontSize = 23, FontWeight = FontWeights.SemiBold });
        details.Children.Add(new TextBlock { Text = $"@{user.Username}  •  {user.Role}{(user.IsPrimary ? "  •  PRIMARY" : string.Empty)}", Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("MutedBrush") });
        var assigned = session.GetControllersForUser(user.SessionId);
        details.Children.Add(new TextBlock
        {
            Text = assigned.Count == 0
                ? "No controller assigned"
                : $"Assigned: {string.Join(", ", assigned.Select(i => i >= 0 && i < connectedControllers.Count && connectedControllers[i] ? $"Controller {i + 1}" : $"Controller {i + 1} disconnected"))}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (Brush)FindResource("MutedBrush")
        });
        root.Children.Add(details);

        var actions = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(actions, 2);
        var profileActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };

        var viewButton = new Button { Content = "View", Width = 88, Height = 42, Margin = new Thickness(4), IsEnabled = profile is not null };
        viewButton.Click += (_, _) => ViewProfileRequested?.Invoke(user.SessionId);
        profileActions.Children.Add(viewButton);

        var canEdit = profile is not null && actor?.GrevId is not null && AccountAuthorizationService.CanEditProfile(actor.Role, actor.GrevId, profile.GrevId);
        var editButton = new Button { Content = "Edit", Width = 88, Height = 42, Margin = new Thickness(4), IsEnabled = canEdit };
        editButton.Click += (_, _) => EditProfileRequested?.Invoke(user.SessionId);
        profileActions.Children.Add(editButton);

        var canManagePlayers = actor is not null && AccountAuthorizationService.Allows(actor.Role, AccountPermission.ManagePlayers);
        var canSignOut = actor?.SessionId == user.SessionId || canManagePlayers;
        var signOutButton = new Button { Content = "Sign Out", Width = 105, Height = 42, Margin = new Thickness(4), IsEnabled = canSignOut };
        signOutButton.Click += (_, _) => SignOutPlayerRequested?.Invoke(user.SessionId);
        profileActions.Children.Add(signOutButton);
        actions.Children.Add(profileActions);

        if (!user.IsPrimary)
        {
            var primaryButton = new Button
            {
                Content = "Make Primary",
                Width = 150,
                Height = 42,
                Margin = new Thickness(4),
                IsEnabled = actor is not null && AccountAuthorizationService.Allows(actor.Role, AccountPermission.ChangePrimaryUser)
            };
            primaryButton.Click += (_, _) => SetPrimaryRequested?.Invoke(user.SessionId);
            actions.Children.Add(primaryButton);
        }

        var controllerButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        for (var controllerIndex = 0; controllerIndex < 4; controllerIndex++)
        {
            var connected = controllerIndex < connectedControllers.Count && connectedControllers[controllerIndex];
            var assignedUser = session.GetUserForController(controllerIndex);
            var assignedToThisUser = assignedUser?.SessionId == user.SessionId;
            if (!connected && !assignedToThisUser) continue;

            var canAssign = actor is not null && AccountAuthorizationService.Allows(actor.Role, AccountPermission.AssignControllers) && (canManagePlayers || actor.SessionId == user.SessionId);
            var request = new PlayerControllerAssignmentRequest(user.SessionId, controllerIndex);
            var label = assignedToThisUser
                ? connected ? $"C{controllerIndex + 1} ✓ Unassign" : $"C{controllerIndex + 1} offline • Unassign"
                : assignedUser is null
                    ? $"C{controllerIndex + 1}"
                    : $"C{controllerIndex + 1} • {assignedUser.DisplayName}";
            var button = new Button
            {
                Content = label,
                MinWidth = assignedToThisUser ? 132 : 72,
                Height = 42,
                Margin = new Thickness(4),
                IsEnabled = canAssign
            };
            if (assignedToThisUser) button.Click += (_, _) => UnassignControllerRequested?.Invoke(request);
            else button.Click += (_, _) => AssignControllerRequested?.Invoke(request);
            controllerButtons.Children.Add(button);
        }

        if (controllerButtons.Children.Count == 0)
        {
            controllerButtons.Children.Add(new TextBlock { Text = "No connected controller available", Margin = new Thickness(4, 8, 4, 4), Foreground = (Brush)FindResource("MutedBrush") });
        }

        actions.Children.Add(controllerButtons);
        root.Children.Add(actions);
        card.Child = root;
        return card;
    }

    private void ApplyPrimaryAvatar(LocalProfile? profile)
    {
        PrimaryAvatarImage.Source = profile is null ? null : ProfileAvatarCatalog.TryLoadCustomImage(profile);
        PrimaryAvatarImage.Visibility = PrimaryAvatarImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryAvatarText.Visibility = PrimaryAvatarImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        PrimaryAvatarText.Text = profile is null ? "?" : ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName);
    }

    private void ApplyPrimaryRole(AccountRole role)
    {
        var roleBrush = GetRoleBrush(role);
        PrimaryProfileCard.BorderBrush = roleBrush;
        PrimaryAvatarBorder.BorderBrush = roleBrush;
        PrimaryProfileCard.Effect = CreateRoleEffect(role, roleBrush.Color);
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
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            BorderBrush = GetRoleBrush(role),
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            Child = grid
        };
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
            BlurRadius = 16,
            ShadowDepth = 0,
            Opacity = 0.48
        },
        AccountRole.Standard => new DropShadowEffect
        {
            Color = color,
            BlurRadius = 7,
            ShadowDepth = 0,
            Opacity = 0.16
        },
        _ => null
    };

    private static LocalProfile? FindProfile(SessionUser? user, IReadOnlyList<LocalProfile> profiles)
    {
        if (user?.GrevId is null) return null;
        return profiles.FirstOrDefault(profile => string.Equals(profile.GrevId, user.GrevId, StringComparison.OrdinalIgnoreCase));
    }

    private void ViewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_primarySessionUserId.HasValue) ViewProfileRequested?.Invoke(_primarySessionUserId.Value);
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_primarySessionUserId.HasValue) EditProfileRequested?.Invoke(_primarySessionUserId.Value);
    }

    private void AddPlayer_Click(object sender, RoutedEventArgs e) => AddPlayerRequested?.Invoke(this, EventArgs.Empty);
    private void Logout_Click(object sender, RoutedEventArgs e) => LogoutRequested?.Invoke(this, EventArgs.Empty);
}
