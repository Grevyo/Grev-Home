using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;
using GrevHome.Sessions;

namespace GrevHome.Views;

public sealed record ProfileSignInRequest(LocalProfile Profile, int? ControllerIndex);

public partial class LoginView : UserControl
{
    public event Action<ProfileSignInRequest>? LocalProfileSignInRequested;
    public event Action<int?>? GuestSignInRequested;
    public event Action<Guid>? PrimaryUserRequested;
    public event EventHandler? CreateProfileRequested;
    public event EventHandler? EnterHomeRequested;
    public event EventHandler? ClearSessionRequested;

    public int? ActivationControllerIndex { get; set; }

    public LoginView()
    {
        InitializeComponent();
    }

    public void Refresh(
        IReadOnlyList<LocalProfile> profiles,
        SessionContext session,
        IReadOnlyList<bool> connectedControllers)
    {
        ProfilesPanel.Children.Clear();
        foreach (var profile in profiles)
        {
            var signedIn = session.SignedInUsers.FirstOrDefault(user => user.GrevId == profile.GrevId);
            var button = new Button
            {
                Width = 235,
                Height = 118,
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
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = signedIn is null ? "Local profile" : BuildSignedInLabel(session, signedIn),
                            Margin = new Thickness(0, 8, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 13
                        }
                    }
                }
            };
            button.Click += LocalProfile_Click;
            ProfilesPanel.Children.Add(button);
        }

        NoProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        SignedInPanel.Children.Clear();
        foreach (var user in session.SignedInUsers)
        {
            var controllerText = BuildControllerList(session, user);
            var button = new Button
            {
                Width = 245,
                Height = 82,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = user.SessionId,
                Content = $"{(user.IsPrimary ? "★ " : string.Empty)}{user.DisplayName}\n{controllerText}"
            };
            button.Click += SignedInUser_Click;
            SignedInPanel.Children.Add(button);
        }

        NoSignedInText.Visibility = session.HasSignedInUsers ? Visibility.Collapsed : Visibility.Visible;
        EnterHomeButton.IsEnabled = session.HasSignedInUsers;
        ClearSessionButton.IsEnabled = session.HasSignedInUsers;

        var controllerLines = new List<string>();
        for (var index = 0; index < connectedControllers.Count; index++)
        {
            if (!connectedControllers[index])
            {
                continue;
            }

            var assigned = session.GetUserForController(index);
            controllerLines.Add($"Controller {index + 1} → {assigned?.DisplayName ?? "Unassigned"}");
        }

        ControllerSummaryText.Text = controllerLines.Count == 0
            ? "No controllers connected"
            : string.Join("     ", controllerLines);
    }

    private static string BuildSignedInLabel(SessionContext session, SessionUser user)
    {
        var controllers = session.GetControllersForUser(user.SessionId);
        return controllers.Count == 0
            ? "Signed in • no controller assigned"
            : $"Signed in • {string.Join(", ", controllers.Select(index => $"Controller {index + 1}"))}";
    }

    private static string BuildControllerList(SessionContext session, SessionUser user)
    {
        var controllers = session.GetControllersForUser(user.SessionId);
        return controllers.Count == 0
            ? "No controller"
            : string.Join(", ", controllers.Select(index => $"Controller {index + 1}"));
    }

    private void LocalProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LocalProfile profile })
        {
            LocalProfileSignInRequested?.Invoke(new ProfileSignInRequest(profile, ActivationControllerIndex));
        }
    }

    private void GuestAccount_Click(object sender, RoutedEventArgs e) =>
        GuestSignInRequested?.Invoke(ActivationControllerIndex);

    private void SignedInUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid sessionUserId })
        {
            PrimaryUserRequested?.Invoke(sessionUserId);
        }
    }

    private void CreateProfile_Click(object sender, RoutedEventArgs e) =>
        CreateProfileRequested?.Invoke(this, EventArgs.Empty);

    private void EnterHome_Click(object sender, RoutedEventArgs e) =>
        EnterHomeRequested?.Invoke(this, EventArgs.Empty);

    private void ClearSession_Click(object sender, RoutedEventArgs e) =>
        ClearSessionRequested?.Invoke(this, EventArgs.Empty);
}
