using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

/// <summary>
/// A read-only detail screen for one friend, opened by clicking their card in FriendsView.
/// Shows only what the Grev.dad friends API actually returns for another account (their public
/// card styling, level/XP, presence, friends-since date, and shared activity) - it does not
/// invent per-app playtime, milestones or sources the way the local ProfileView does, because
/// none of that is synced for other accounts today.
/// </summary>
public partial class FriendProfileView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? MessageRequested;

    public FriendProfileView()
    {
        InitializeComponent();
    }

    public void SetFriend(GrevDadFriend friend, bool isSelf = false)
    {
        MessageButton.Visibility = isSelf ? Visibility.Collapsed : Visibility.Visible;
        var card = friend.PublicCard ?? new GrevDadPublicCard();

        ProfileAvatarShapeStyle.Apply(AvatarBorder, card.AvatarShape, AvatarBorder.Width);
        AvatarText.Text = string.IsNullOrWhiteSpace(friend.DisplayName) ? "?" : friend.DisplayName[..1].ToUpperInvariant();
        AvatarImage.Source = ProfileAvatarShapeStyle.TryLoadDataUrl(card.AvatarMedia);
        AvatarImage.Visibility = AvatarImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        AvatarText.Visibility = AvatarImage.Source is null ? Visibility.Visible : Visibility.Collapsed;

        DisplayNameText.Text = friend.DisplayName;
        BioText.Text = card.Bio;
        VerifiedText.Visibility = friend.IsVerified ? Visibility.Visible : Visibility.Collapsed;
        UsernameText.Text = card.ShowUsername ? $"@{friend.Username}" : string.Empty;
        UsernameText.Visibility = card.ShowUsername ? Visibility.Visible : Visibility.Collapsed;

        if (card.ShowStatus)
        {
            StatusText.Text = string.IsNullOrWhiteSpace(friend.Presence.ActivityText)
                ? (string.IsNullOrWhiteSpace(card.StatusMessage) ? friend.Presence.Availability : card.StatusMessage)
                : $"{friend.Presence.Availability} • {friend.Presence.ActivityText}";
        }
        else
        {
            StatusText.Text = string.Empty;
        }

        LevelText.Text = card.ShowLevel
            ? (card.ShowXp ? $"Level {friend.Level}  •  {friend.TotalXp:N0} XP" : $"Level {friend.Level}")
            : card.ShowXp ? $"{friend.TotalXp:N0} XP" : "Hidden";

        FriendsSinceText.Text = friend.FriendsSinceUtc.ToLocalTime().ToString("d MMM yyyy");

        HeaderCard.Background = PublicProfileCardStyle.Background(card);
        HeaderCard.Effect = PublicProfileCardStyle.FrameEffect(card.Frame);
        HeaderCard.BorderThickness = card.Frame switch
        {
            "clean" => new Thickness(0),
            "double" => new Thickness(5),
            _ => new Thickness(2)
        };
    }

    public void SetActivity(IReadOnlyList<GrevDadActivityEvent> events)
    {
        ActivityPanel.Children.Clear();
        NoActivityText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var activity in events)
        {
            ActivityPanel.Children.Add(CreateActivityRow(activity));
        }
    }

    private Border CreateActivityRow(GrevDadActivityEvent activity)
    {
        var verb = string.Equals(activity.Type, "app.started", StringComparison.OrdinalIgnoreCase)
            ? "Started"
            : string.Equals(activity.Type, "app.stopped", StringComparison.OrdinalIgnoreCase)
                ? "Stopped"
                : activity.Type;
        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{verb} {activity.AppName}",
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = activity.OccurredAtUtc.ToLocalTime().ToString("d MMM yyyy, h:mm tt"),
                        Margin = new Thickness(0, 4, 0, 0),
                        FontSize = 11,
                        Foreground = (Brush)FindResource("MutedBrush")
                    }
                }
            }
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Message_Click(object sender, RoutedEventArgs e) => MessageRequested?.Invoke(this, EventArgs.Empty);
}
