using System.Windows;
using System.Windows.Controls;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record PlayerControllerAssignmentRequest(Guid SessionUserId, int ControllerIndex);

public partial class ProfilePlayersView : UserControl
{
    public event EventHandler? ViewProfileRequested;
    public event EventHandler? EditProfileRequested;
    public event EventHandler? AddPlayerRequested;
    public event EventHandler? LogoutRequested;
    public event Action<Guid>? SetPrimaryRequested;
    public event Action<PlayerControllerAssignmentRequest>? AssignControllerRequested;

    public ProfilePlayersView()
    {
        InitializeComponent();
    }

    public void SetState(SessionContext session, IReadOnlyList<bool> connectedControllers)
    {
        var primary = session.PrimaryUser;
        PrimaryNameText.Text = primary?.DisplayName ?? "No primary profile";
        PrimaryIdentityText.Text = primary is null
            ? "No user is signed in."
            : $"@{primary.Username}  •  {primary.Role}  •  Primary User";

        SummaryText.Text = session.SignedInUsers.Count == 1
            ? "1 player signed in. Add Player 2 or manage the current controller assignment."
            : $"{session.SignedInUsers.Count} players signed in. Manage Primary User and controller assignments here.";

        AddPlayerButton.Content = $"Player {session.SignedInUsers.Count + 1} Sign In";
        AddPlayerButton.IsEnabled = session.SignedInUsers.Count < 4;

        PlayersPanel.Children.Clear();
        for (var index = 0; index < session.SignedInUsers.Count; index++)
        {
            PlayersPanel.Children.Add(CreatePlayerCard(index + 1, session.SignedInUsers[index], session, connectedControllers));
        }

        StatusText.Text = connectedControllers.Any(isConnected => isConnected)
            ? "Select a connected Controller button on a player card to assign or reassign it. Controller ownership updates immediately."
            : "No XInput controllers are currently connected. Connect a controller, then return here to assign it.";
    }

    private UIElement CreatePlayerCard(
        int playerNumber,
        SessionUser user,
        SessionContext session,
        IReadOnlyList<bool> connectedControllers)
    {
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
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = $"PLAYER {playerNumber}",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush")
        });
        details.Children.Add(new TextBlock
        {
            Text = user.DisplayName,
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 23,
            FontWeight = FontWeights.SemiBold
        });
        details.Children.Add(new TextBlock
        {
            Text = $"@{user.Username}  •  {user.Role}{(user.IsPrimary ? "  •  PRIMARY" : string.Empty)}",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
        });

        var assigned = session.GetControllersForUser(user.SessionId);
        details.Children.Add(new TextBlock
        {
            Text = assigned.Count == 0
                ? "No controller assigned"
                : $"Assigned: {string.Join(", ", assigned.Select(controllerIndex => $"Controller {controllerIndex + 1}"))}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
        });

        root.Children.Add(details);

        var actions = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0)
        };
        Grid.SetColumn(actions, 1);

        if (!user.IsPrimary)
        {
            var primaryButton = new Button
            {
                Content = "Make Primary",
                Width = 150,
                Height = 44,
                Margin = new Thickness(4)
            };
            primaryButton.Click += (_, _) => SetPrimaryRequested?.Invoke(user.SessionId);
            actions.Children.Add(primaryButton);
        }

        var controllerButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        for (var controllerIndex = 0; controllerIndex < connectedControllers.Count; controllerIndex++)
        {
            if (!connectedControllers[controllerIndex])
            {
                continue;
            }

            var assignedUser = session.GetUserForController(controllerIndex);
            var assignedToThisUser = assignedUser?.SessionId == user.SessionId;
            var button = new Button
            {
                Content = assignedToThisUser
                    ? $"C{controllerIndex + 1} ✓"
                    : assignedUser is null
                        ? $"C{controllerIndex + 1}"
                        : $"C{controllerIndex + 1} • {assignedUser.DisplayName}",
                MinWidth = 72,
                Height = 44,
                Margin = new Thickness(4),
                Tag = new PlayerControllerAssignmentRequest(user.SessionId, controllerIndex),
                IsEnabled = !assignedToThisUser
            };
            button.Click += Controller_Click;
            controllerButtons.Children.Add(button);
        }

        if (controllerButtons.Children.Count == 0)
        {
            controllerButtons.Children.Add(new TextBlock
            {
                Text = "No controllers connected",
                Margin = new Thickness(4, 8, 4, 4),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
            });
        }

        actions.Children.Add(controllerButtons);
        root.Children.Add(actions);
        card.Child = root;
        return card;
    }

    private void Controller_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlayerControllerAssignmentRequest request })
        {
            AssignControllerRequested?.Invoke(request);
        }
    }

    private void ViewProfile_Click(object sender, RoutedEventArgs e) =>
        ViewProfileRequested?.Invoke(this, EventArgs.Empty);

    private void EditProfile_Click(object sender, RoutedEventArgs e) =>
        EditProfileRequested?.Invoke(this, EventArgs.Empty);

    private void AddPlayer_Click(object sender, RoutedEventArgs e) =>
        AddPlayerRequested?.Invoke(this, EventArgs.Empty);

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);
}
