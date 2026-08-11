using System.Windows;
using System.Windows.Controls;
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
            ? "Select an assigned C button to unassign that controller without signing the player out. Controllers can then be assigned again at any time."
            : "No XInput controllers are currently connected. Players stay signed in even with no controller assigned.";
    }

    private UIElement CreatePlayerCard(int playerNumber, SessionUser user, SessionContext session, IReadOnlyList<bool> connectedControllers, IReadOnlyList<LocalProfile> profiles, SessionUser? actor)
    {
        var profile = FindProfile(user, profiles);
        var card = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 21, 30)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(CreateAvatar(profile, 66));

        var details = new StackPanel();
        Grid.SetColumn(details, 1);
        details.Children.Add(new TextBlock { Text = $"PLAYER {playerNumber}", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush") });
        details.Children.Add(new TextBlock { Text = user.DisplayName, Margin = new Thickness(0, 5, 0, 0), FontSize = 23, FontWeight = FontWeights.SemiBold });
        details.Children.Add(new TextBlock { Text = $"@{user.Username}  •  {user.Role}{(user.IsPrimary ? "  •  PRIMARY" : string.Empty)}", Margin = new Thickness(0, 4, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        var assigned = session.GetControllersForUser(user.SessionId);
        details.Children.Add(new TextBlock { Text = assigned.Count == 0 ? "No controller assigned" : $"Assigned: {string.Join(", ", assigned.Select(i => $"Controller {i + 1}"))}", Margin = new Thickness(0, 5, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
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
        for (var controllerIndex = 0; controllerIndex < connectedControllers.Count; controllerIndex++)
        {
            if (!connectedControllers[controllerIndex]) continue;

            var assignedUser = session.GetUserForController(controllerIndex);
            var assignedToThisUser = assignedUser?.SessionId == user.SessionId;
            var canAssign = actor is not null && AccountAuthorizationService.Allows(actor.Role, AccountPermission.AssignControllers) && (canManagePlayers || actor.SessionId == user.SessionId);
            var request = new PlayerControllerAssignmentRequest(user.SessionId, controllerIndex);
            var button = new Button
            {
                Content = assignedToThisUser ? $"C{controllerIndex + 1} ✓ Unassign" : assignedUser is null ? $"C{controllerIndex + 1}" : $"C{controllerIndex + 1} • {assignedUser.DisplayName}",
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
            controllerButtons.Children.Add(new TextBlock { Text = "No controllers connected", Margin = new Thickness(4, 8, 4, 4), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
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

    private static Border CreateAvatar(LocalProfile? profile, double size)
    {
        var imageSource = profile is null ? null : ProfileAvatarCatalog.TryLoadCustomImage(profile);
        var grid = new Grid();
        if (imageSource is not null)
        {
            grid.Children.Add(new Image
            {
                Source = imageSource,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                Clip = new System.Windows.Media.EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2)
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
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 40, 58)),
            ClipToBounds = true,
            Child = grid
        };
    }

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
