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

    public FriendsView()
    {
        InitializeComponent();
        FriendCodeKeyboard.Completed += code => AddFriendCodeRequested?.Invoke(code);
    }

    public void SetFriends(string accountName, string? friendCode, IReadOnlyList<GrevDadFriend> friends,
        GrevDadFriendRequestsSnapshot requests, bool offline)
    {
        _friendCode = friendCode;
        FriendCodeText.Text = string.IsNullOrWhiteSpace(friendCode) ? "Generating…" : friendCode;
        ContextText.Text = offline ? $"{accountName} • Grev.dad offline • showing cached friends" : $"{accountName} • Grev.dad connected";
        FriendsPanel.Children.Clear();
        foreach (var friend in friends.OrderByDescending(item => item.Presence.Availability != "offline").ThenBy(item => item.DisplayName))
            FriendsPanel.Children.Add(CreateFriendCard(friend));
        EmptyText.Visibility = friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RequestsPanel.Children.Clear();
        foreach (var request in requests.Incoming) RequestsPanel.Children.Add(CreateRequestCard(request, true));
        foreach (var request in requests.Outgoing) RequestsPanel.Children.Add(CreateRequestCard(request, false));
        NoRequestsText.Visibility = requests.Incoming.Count + requests.Outgoing.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = string.Empty;
    }

    private Border CreateFriendCard(GrevDadFriend friend)
    {
        var card = friend.PublicCard ?? new GrevDadPublicCard();
        var details = string.IsNullOrWhiteSpace(friend.Presence.ActivityText) ? friend.Presence.Availability : $"{friend.Presence.Availability} • {friend.Presence.ActivityText}";
        return new Border { Width=300,Height=154,Margin=new Thickness(8),Padding=new Thickness(18),Background=ProfileBannerCatalog.CreateBrush(card.Theme),
            BorderBrush=new SolidColorBrush(Color.FromRgb(72,96,142)),BorderThickness=card.Frame=="clean"?new Thickness(0):card.Frame=="double"?new Thickness(5):new Thickness(2),CornerRadius=new CornerRadius(0),
            Child=new StackPanel { Children={ new TextBlock { Text=friend.DisplayName,FontSize=22,FontWeight=FontWeights.SemiBold },
                new TextBlock { Text=card.ShowUsername?$"@{friend.Username}":string.Empty,Margin=new Thickness(0,4,0,0),Foreground=(Brush)FindResource("MutedBrush") },
                new TextBlock { Text=card.ShowLevel?(card.ShowXp?$"Level {friend.Level}  •  {friend.TotalXp:N0} XP":$"Level {friend.Level}"):card.ShowXp?$"{friend.TotalXp:N0} XP":string.Empty,Margin=new Thickness(0,6,0,0),FontSize=12 },
                new TextBlock { Text=card.ShowStatus?details:friend.Presence.Availability,Margin=new Thickness(0,10,0,0),TextTrimming=TextTrimming.CharacterEllipsis } } } };
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
