using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class FriendsView : UserControl
{
    private string? _friendCode;
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
        foreach (var friend in friends.OrderByDescending(item => item.Presence.Availability != "offline").ThenByDescending(item => item.Presence.UpdatedAtUtc).ThenBy(item => item.DisplayName))
            FriendsPanel.Children.Add(CreateFriendCard(friend));
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

    public Button CreateFriendCard(GrevDadFriend friend, bool isSelf = false)
    {
        var card = friend.PublicCard ?? new GrevDadPublicCard();
        var details = string.IsNullOrWhiteSpace(friend.Presence.ActivityText) ? friend.Presence.Availability : $"{friend.Presence.Availability} • {friend.Presence.ActivityText}";
        var frameThickness = card.Frame == "clean" ? new Thickness(0) : card.Frame == "double" ? new Thickness(5) : new Thickness(2);

        // Background/BorderBrush/etc. live in a Style (based on SharpTileButtonStyle) rather than as
        // local values on the Button, so SharpTileButtonStyle's inherited focus/hover triggers can
        // still override the border to show a controller focus ring - a local value would suppress
        // those triggers outright, leaving cards with no visible focus indicator when D-pad navigated.
        var style = new Style(typeof(Button), (Style)FindResource("SharpTileButtonStyle"));
        var cover = ProfileAvatarShapeStyle.TryLoadDataUrl(card.CoverMedia);
        style.Setters.Add(new Setter(Control.BackgroundProperty, cover is null
            ? ProfileBannerCatalog.CreateBrush(card.Theme)
            : new ImageBrush(cover) { Stretch = Stretch.UniformToFill, Opacity = 0.72 }));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(72, 96, 142))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, frameThickness));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));

        var avatar = new Border
        {
            Width = 40,
            Height = 40,
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            Child = CreateAvatarContent(friend, card)
        };
        ProfileAvatarShapeStyle.Apply(avatar, card.AvatarShape, 40);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                avatar,
                new TextBlock
                {
                    Text = isSelf ? $"{friend.DisplayName} (You)" : friend.DisplayName,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var button = new Button
        {
            Width = 300,
            Height = 154,
            Margin = new Thickness(8),
            Style = style,
            Content = new StackPanel
            {
                Children =
                {
                    header,
                    new TextBlock { Text=card.ShowUsername?$"@{friend.Username}":string.Empty,Margin=new Thickness(0,8,0,0),Foreground=(Brush)FindResource("MutedBrush") },
                    new TextBlock { Text=card.ShowLevel?(card.ShowXp?$"Level {friend.Level}  •  {friend.TotalXp:N0} XP":$"Level {friend.Level}"):card.ShowXp?$"{friend.TotalXp:N0} XP":string.Empty,Margin=new Thickness(0,6,0,0),FontSize=12 },
                    new TextBlock { Text=card.ShowStatus?details:string.Empty,Margin=new Thickness(0,10,0,0),TextTrimming=TextTrimming.CharacterEllipsis }
                }
            }
        };
        button.Click += (_, _) => FriendSelected?.Invoke(friend);
        return button;
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
    private void OpenFriendCodeKeyboard_Click(object sender,RoutedEventArgs e)=>FriendCodeKeyboard.Open("Enter Friend Code","GREV-",14);
    private void CopyCode_Click(object sender,RoutedEventArgs e){if(string.IsNullOrWhiteSpace(_friendCode)){ShowStatus("Your friend code is not available yet. Refresh after Grev.dad reconnects.");return;}try{Clipboard.SetText(_friendCode);ShowStatus("Friend code copied.");}catch(System.Runtime.InteropServices.ExternalException){ShowStatus("The clipboard is busy. Try again.");}}
    private void Refresh_Click(object sender,RoutedEventArgs e)=>RefreshRequested?.Invoke(this,EventArgs.Empty);
    private void Back_Click(object sender,RoutedEventArgs e)=>BackRequested?.Invoke(this,EventArgs.Empty);
}
