using System.Windows;
using System.Windows.Controls;
using GrevHome.Online;

namespace GrevHome.Views;

public partial class FriendsView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? RefreshRequested;
    public FriendsView() => InitializeComponent();
    public void SetFriends(string accountName, IReadOnlyList<GrevDadFriend> friends, bool offline)
    {
        ContextText.Text = offline ? $"{accountName} • Grev.dad offline • showing cached friends" : $"{accountName} • Grev.dad connected";
        FriendsPanel.Children.Clear();
        foreach (var friend in friends.OrderByDescending(item => item.Presence.Availability != "offline").ThenBy(item => item.DisplayName))
        {
            FriendsPanel.Children.Add(new Border { Width = 285, Height = 120, Margin = new Thickness(8), Padding = new Thickness(18), Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"), CornerRadius = new CornerRadius(12), Child = new StackPanel { Children = { new TextBlock { Text = friend.DisplayName, FontSize = 20, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = $"@{friend.Username}", Margin = new Thickness(0,4,0,0), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") }, new TextBlock { Text = string.IsNullOrWhiteSpace(friend.Presence.ActivityText) ? friend.Presence.Availability : $"{friend.Presence.Availability} • {friend.Presence.ActivityText}", Margin = new Thickness(0,8,0,0), TextTrimming = TextTrimming.CharacterEllipsis } } } });
        }
        EmptyText.Visibility = friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = string.Empty;
    }
    public void ShowStatus(string message) => StatusText.Text = message;
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
