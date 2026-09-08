using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

/// <summary>
/// Read-only full profile for a Grev.dad friend. It deliberately renders only the public card,
/// account progression, account-wide Grev Home totals and friend-visible activity returned by
/// the Grev.dad contract. Private per-app history, milestones and source details remain local.
/// </summary>
public partial class FriendProfileView : UserControl
{
    private bool _isSelfPreview;
    public event EventHandler? BackRequested;
    public event EventHandler? MessageRequested;

    public FriendProfileView()
    {
        InitializeComponent();
    }

    public void SetFriend(GrevDadFriend friend, bool isSelf = false)
    {
        _isSelfPreview = isSelf;
        MessageButton.Visibility = isSelf ? Visibility.Collapsed : Visibility.Visible;
        PreviewText.Text = isSelf
            ? "YOUR PUBLIC CARD PREVIEW • LIVE ACCOUNT TOTALS ARE SUPPLIED BY GREV.DAD"
            : "YOUR PUBLIC CARD PREVIEW";
        PreviewText.Visibility = isSelf ? Visibility.Visible : Visibility.Collapsed;
        var card = friend.PublicCard ?? new GrevDadPublicCard();

        ProfileAvatarShapeStyle.Apply(AvatarBorder, card.AvatarShape, AvatarBorder.Width);
        AvatarText.Text = string.IsNullOrWhiteSpace(friend.DisplayName) ? "?" : friend.DisplayName[..1].ToUpperInvariant();
        AvatarImage.Source = ProfileAvatarShapeStyle.TryLoadDataUrl(card.AvatarMedia);
        AvatarImage.Visibility = AvatarImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        AvatarText.Visibility = AvatarImage.Source is null ? Visibility.Visible : Visibility.Collapsed;

        DisplayNameText.Text = friend.DisplayName;
        BioText.Text = card.Bio;
        BioText.Visibility = string.IsNullOrWhiteSpace(card.Bio) ? Visibility.Collapsed : Visibility.Visible;
        VerifiedText.Visibility = friend.IsVerified ? Visibility.Visible : Visibility.Collapsed;
        UsernameText.Text = card.ShowUsername ? $"@{friend.Username}" : string.Empty;
        UsernameText.Visibility = card.ShowUsername ? Visibility.Visible : Visibility.Collapsed;

        if (card.ShowStatus)
        {
            StatusText.Text = string.IsNullOrWhiteSpace(friend.Presence.ActivityText)
                ? (string.IsNullOrWhiteSpace(card.StatusMessage) ? friend.Presence.Availability : card.StatusMessage)
                : $"{friend.Presence.Availability} • {friend.Presence.ActivityText}";
            StatusText.Foreground = friend.Presence.Availability == "offline"
                ? (Brush)FindResource("MutedBrush")
                : (Brush)FindResource("AccentBrush");
            StatusText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Text = string.Empty;
            StatusText.Visibility = Visibility.Collapsed;
        }

        LevelXpCard.Visibility = card.ShowLevel || card.ShowXp ? Visibility.Visible : Visibility.Collapsed;
        LevelText.Text = card.ShowLevel
            ? (card.ShowXp ? $"Level {friend.Level}  •  {friend.TotalXp:N0} XP" : $"Level {friend.Level}")
            : $"{friend.TotalXp:N0} XP";

        // Only real friend payloads contain account-wide totals. The local self-preview is about
        // appearance/privacy and must not pretend an absent server projection means zero activity.
        PlayTimeCard.Visibility = !isSelf && card.ShowPlaytime ? Visibility.Visible : Visibility.Collapsed;
        PlayTimeText.Text = FormatDuration(card.TotalTrackedSeconds);
        SessionsCard.Visibility = !isSelf && card.ShowSessions ? Visibility.Visible : Visibility.Collapsed;
        SessionsText.Text = card.CompletedSessions.ToString("N0");

        RelationshipLabel.Text = isSelf ? "VIEW MODE" : "FRIENDS SINCE";
        FriendsSinceText.Text = isSelf
            ? "Public preview"
            : friend.FriendsSinceUtc.ToLocalTime().ToString("d MMM yyyy");

        HeaderCard.Background = PublicProfileCardStyle.Background(card);
        HeaderCard.Effect = PublicProfileCardStyle.FrameEffect(card.Frame);
        HeaderCard.BorderThickness = card.Frame switch
        {
            "clean" => new Thickness(0),
            "double" => new Thickness(5),
            _ => new Thickness(2)
        };

        ActivityHeadingText.Visibility = isSelf ? Visibility.Collapsed : Visibility.Visible;
        ActivityPanel.Visibility = isSelf ? Visibility.Collapsed : Visibility.Visible;
        NoActivityText.Visibility = isSelf ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetActivity(IReadOnlyList<GrevDadActivityEvent> events)
    {
        ActivityPanel.Children.Clear();
        if (_isSelfPreview)
        {
            ActivityHeadingText.Visibility = Visibility.Collapsed;
            ActivityPanel.Visibility = Visibility.Collapsed;
            NoActivityText.Visibility = Visibility.Collapsed;
            return;
        }

        ActivityHeadingText.Visibility = Visibility.Visible;
        ActivityPanel.Visibility = Visibility.Visible;
        NoActivityText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var activity in events)
        {
            ActivityPanel.Children.Add(CreateActivityRow(activity));
        }
    }

    private static string FormatDuration(long seconds)
    {
        seconds = Math.Max(0, seconds);
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 100) return $"{Math.Floor(span.TotalHours):N0} hours";
        if (span.TotalHours >= 1) return $"{Math.Floor(span.TotalHours):N0}h {span.Minutes}m";
        return $"{Math.Max(0, span.Minutes):N0} minutes";
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
