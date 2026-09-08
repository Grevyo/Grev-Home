using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class FriendsView : UserControl
{
    private string? _friendCode;
    private readonly Dictionary<string, Button> _friendButtons = new();
    public event EventHandler? BackRequested;
    public event EventHandler? RefreshRequested;
    public event Action<string>? AddFriendCodeRequested;
    public event Action<string>? AcceptRequestRequested;
    public event Action<string>? DeclineRequestRequested;
    public event Action<string>? CancelRequestRequested;
    public event Action<GrevDadFriend>? FriendSelected;

    public FriendsView()
    {
        InitializeComponent();
        FriendCodeKeyboard.Completed += code => AddFriendCodeRequested?.Invoke(code);
    }

    public void SetFriends(string accountName, string? friendCode, IReadOnlyList<GrevDadFriend> friends,
        GrevDadFriendRequestsSnapshot requests, bool offline, GrevDadFriend? self = null)
    {
        _friendCode = friendCode;
        FriendCodeText.Text = string.IsNullOrWhiteSpace(friendCode) ? "Generating…" : friendCode;
        ContextText.Text = offline ? $"{accountName} • Grev.dad offline • showing cached friends" : $"{accountName} • Grev.dad connected";
        FriendsPanel.Children.Clear();
        _friendButtons.Clear();
        foreach (var friend in friends.OrderByDescending(item => item.Presence.Availability != "offline").ThenByDescending(item => item.Presence.UpdatedAtUtc).ThenBy(item => item.DisplayName))
        {
            var button = CreateFriendCard(friend);
            _friendButtons[friend.UserId] = button;
            FriendsPanel.Children.Add(button);
        }
        // Your own preview card always sits last, regardless of sort order, so it reads as "and
        // here's you" rather than competing with real friends for a spot based on name/presence.
        if (self is not null) FriendsPanel.Children.Add(CreateFriendCard(self, isSelf: true));
        EmptyText.Visibility = friends.Count == 0 && self is null ? Visibility.Visible : Visibility.Collapsed;
        RequestsPanel.Children.Clear();
        foreach (var request in requests.Incoming) RequestsPanel.Children.Add(CreateRequestCard(request, true));
        foreach (var request in requests.Outgoing) RequestsPanel.Children.Add(CreateRequestCard(request, false));
        NoRequestsText.Visibility = requests.Incoming.Count + requests.Outgoing.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = string.Empty;
    }

    public Button CreateFriendCard(GrevDadFriend friend, bool isSelf = false) => CreateFriendCard(friend, this, selected => FriendSelected?.Invoke(selected), isSelf);

    public static Button CreateFriendCard(GrevDadFriend friend, FrameworkElement resources, Action<GrevDadFriend> selected, bool isSelf = false)
    {
        var card = friend.PublicCard ?? new GrevDadPublicCard();
        var details = string.IsNullOrWhiteSpace(friend.Presence.ActivityText)
            ? (string.IsNullOrWhiteSpace(card.StatusMessage) ? friend.Presence.Availability : card.StatusMessage)
            : $"{friend.Presence.Availability} • {friend.Presence.ActivityText}";
        var frameThickness = card.Frame == "clean" ? new Thickness(0) : card.Frame == "double" ? new Thickness(5) : new Thickness(2);

        // Background/BorderBrush/etc. live in a Style (based on SharpTileButtonStyle) rather than as
        // local values on the Button, so SharpTileButtonStyle's inherited focus/hover triggers can
        // still override the border to show a controller focus ring.
        var style = new Style(typeof(Button), (Style)resources.FindResource("SharpTileButtonStyle"));
        style.Setters.Add(new Setter(Control.BackgroundProperty, PublicProfileCardStyle.Background(card)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(72, 96, 142))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, frameThickness));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));

        var avatar = new Border
        {
            Width = 42,
            Height = 42,
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            Child = CreateAvatarContent(friend, card)
        };
        ProfileAvatarShapeStyle.Apply(avatar, card.AvatarShape, 42);
        DockPanel.SetDock(avatar, Dock.Left);

        var nameStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = isSelf ? $"{friend.DisplayName}  •  YOUR PREVIEW" : friend.DisplayName,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (friend.IsVerified)
        {
            nameStack.Children.Add(new TextBlock
            {
                Text = "✓ VERIFIED GREV.DAD MEMBER",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)resources.FindResource("AccentBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        var header = new DockPanel { LastChildFill = true, Children = { avatar, nameStack } };
        var stats = new List<string>();
        if (card.ShowLevel) stats.Add($"Level {friend.Level}");
        if (card.ShowXp) stats.Add($"{friend.TotalXp:N0} XP");
        // The self tile is a local style preview. Real friends receive account-wide totals from
        // grev.dad, so do not render a misleading local/default zero here.
        if (!isSelf && card.ShowPlaytime) stats.Add(FormatDurationCompact(card.TotalTrackedSeconds));
        if (!isSelf && card.ShowSessions) stats.Add($"{card.CompletedSessions:N0} sessions");

        var bioText = new TextBlock
        {
            Text = card.Bio,
            MaxHeight = 34,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 7, 0, 0),
            FontSize = 12,
            Visibility = string.IsNullOrWhiteSpace(card.Bio) ? Visibility.Collapsed : Visibility.Visible
        };
        var usernameText = new TextBlock
        {
            Text = card.ShowUsername ? $"@{friend.Username}" : string.Empty,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)resources.FindResource("MutedBrush"),
            Visibility = card.ShowUsername ? Visibility.Visible : Visibility.Collapsed
        };
        var statsText = new TextBlock
        {
            Text = string.Join("  •  ", stats),
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = stats.Count == 0 ? Visibility.Collapsed : Visibility.Visible
        };
        var statusText = new TextBlock
        {
            Text = card.ShowStatus ? details : string.Empty,
            Margin = new Thickness(0, 9, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = friend.Presence.Availability == "offline"
                ? (Brush)resources.FindResource("MutedBrush")
                : (Brush)resources.FindResource("AccentBrush"),
            Visibility = card.ShowStatus ? Visibility.Visible : Visibility.Collapsed
        };

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(bioText);
        content.Children.Add(usernameText);
        content.Children.Add(statsText);
        content.Children.Add(statusText);
        if (isSelf && (card.ShowPlaytime || card.ShowSessions))
        {
            content.Children.Add(new TextBlock
            {
                Text = "Live account totals are supplied by grev.dad to your friends.",
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 10,
                Foreground = (Brush)resources.FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var button = new Button
        {
            Width = 310,
            Height = isSelf && (card.ShowPlaytime || card.ShowSessions) ? 242 : 226,
            Effect = PublicProfileCardStyle.FrameEffect(card.Frame),
            Margin = new Thickness(8),
            Style = style,
            Content = content
        };
        button.Click += (_, _) => selected(friend);
        return button;
    }

    private static string FormatDurationCompact(long seconds)
    {
        seconds = Math.Max(0, seconds);
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 100) return $"{Math.Floor(span.TotalHours):N0}h played";
        if (span.TotalHours >= 1) return $"{Math.Floor(span.TotalHours):N0}h {span.Minutes}m played";
        return $"{Math.Max(0, span.Minutes):N0}m played";
    }

    private static Grid CreateAvatarContent(GrevDadFriend friend, GrevDadPublicCard card)
    {
        var grid = new Grid();
        var image = ProfileAvatarShapeStyle.TryLoadDataUrl(card.AvatarMedia);
        if (image is not null) grid.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
        else grid.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(friend.DisplayName) ? "?" : friend.DisplayName[..1].ToUpperInvariant(),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return grid;
    }

    private Border CreateRequestCard(GrevDadFriendRequest request, bool incoming)
    {
        var actions=new WrapPanel();
        if(incoming){actions.Children.Add(ActionButton("Accept",()=>AcceptRequestRequested?.Invoke(request.Id)));actions.Children.Add(ActionButton("Decline",()=>DeclineRequestRequested?.Invoke(request.Id)));}
        else actions.Children.Add(ActionButton("Cancel",()=>CancelRequestRequested?.Invoke(request.Id)));
        return new Border { Width=285,Margin=new Thickness(8),Padding=new Thickness(15),Background=(Brush)FindResource("SurfaceBrush"),Child=new StackPanel { Children={
            new TextBlock { Text=request.User.DisplayName,FontSize=18,FontWeight=FontWeights.SemiBold },new TextBlock { Text=incoming?"Wants to be friends":"Request sent",Foreground=(Brush)FindResource("MutedBrush"),Margin=new Thickness(0,3,0,8) },actions } } };
    }

    private static Button ActionButton(string label, Action action){var button=new Button{Content=label,Width=105,Height=42,Margin=new Thickness(2)};button.Click+=(_,_)=>action();return button;}
    public void ShowStatus(string message)=>StatusText.Text=message;
    public void SetUnreadMessages(IReadOnlyList<GrevDadConversation> conversations)
    {
        var total = conversations.Sum(item => item.Unread);
        if (total > 0) StatusText.Text = $"{total} unread messages — open a friend's profile to read and reply.";
        foreach (var conversation in conversations.Where(item => item.Unread > 0))
        {
            if (!_friendButtons.TryGetValue(conversation.UserId, out var button) || button.Content is not StackPanel panel) continue;
            panel.Children.Add(new TextBlock { Text=$"{conversation.Unread} unread messages", FontWeight=FontWeights.Bold, Foreground=(Brush)FindResource("AccentBrush"), Margin=new Thickness(0,6,0,0) });
            button.Height = Math.Max(button.Height, 248);
        }
    }
    private void OpenFriendCodeKeyboard_Click(object sender,RoutedEventArgs e)=>FriendCodeKeyboard.Open("Enter Friend Code","GREV-",14);
    private void CopyCode_Click(object sender,RoutedEventArgs e){if(string.IsNullOrWhiteSpace(_friendCode)){ShowStatus("Your friend code is not available yet. Refresh after Grev.dad reconnects.");return;}try{Clipboard.SetText(_friendCode);ShowStatus("Friend code copied.");}catch(System.Runtime.InteropServices.ExternalException){ShowStatus("The clipboard is busy. Try again.");}}
    private void Refresh_Click(object sender,RoutedEventArgs e)=>RefreshRequested?.Invoke(this,EventArgs.Empty);
    private void Back_Click(object sender,RoutedEventArgs e)=>BackRequested?.Invoke(this,EventArgs.Empty);
}
