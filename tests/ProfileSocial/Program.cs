using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using GrevHome.Online;
using GrevHome.Profiles;
using GrevHome.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load("src/GrevHome/App.xaml");
        var resources = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            source.Root!.Element(presentation + "Application.Resources")!.Nodes());
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());

        var publicCard = new GrevDadPublicCard(
            Theme: "ocean",
            Frame: "glow",
            AvatarShape: "rounded",
            ShowUsername: true,
            ShowLevel: true,
            ShowXp: true,
            ShowPlaytime: true,
            ShowSessions: true,
            ShowStatus: true,
            Bio: "Public bio",
            StatusMessage: "Ready to play",
            TotalTrackedSeconds: 4500,
            CompletedSessions: 6);
        var friend = new GrevDadFriend(
            "friend-id",
            "friend",
            "Friend Name",
            true,
            DateTimeOffset.UtcNow.AddDays(-30),
            new GrevDadPresence("online", "", "game", "Playing CS2", null, DateTimeOffset.UtcNow),
            publicCard,
            1250,
            3);

        var host = new FriendsView();
        var friendCard = FriendsView.CreateFriendCard(friend, host, _ => { });
        Check(friendCard.Effect is not null, "Glow frame must remain visible on friend cards.");
        Check(friendCard.Content is StackPanel, "Friend card must keep a controller-friendly single content surface.");
        var cardText = string.Join(" | ", TextBlocks((DependencyObject)friendCard.Content).Select(text => text.Text));
        Check(cardText.Contains("VERIFIED", StringComparison.Ordinal), "Verified friends must be identifiable on the compact card.");
        Check(cardText.Contains("Level 3", StringComparison.Ordinal) && cardText.Contains("1,250 XP", StringComparison.Ordinal), "Level and XP visibility options must render real shared progression.");
        Check(cardText.Contains("1h 15m played", StringComparison.Ordinal), "Show play time must render the public Grev Home total.");
        Check(cardText.Contains("6 sessions", StringComparison.Ordinal), "Show sessions must render the public Grev Home session total.");
        Check(cardText.Contains("Playing CS2", StringComparison.Ordinal), "Friend presence/activity must remain visible when status sharing is enabled.");

        var full = new FriendProfileView();
        full.SetFriend(friend);
        Check(((TextBlock)full.FindName("PlayTimeText")).Text == "1h 15m", "Full friend profile must show the same play time as the compact card.");
        Check(((TextBlock)full.FindName("SessionsText")).Text == "6", "Full friend profile must show the same session count as the compact card.");
        Check(((TextBlock)full.FindName("VerifiedText")).Visibility == Visibility.Visible, "Verified marker must carry through to the full profile.");
        Check(((TextBlock)full.FindName("BioText")).Text == "Public bio", "Bio customisation must carry through to the full profile.");

        var privateCard = publicCard with
        {
            ShowUsername = false,
            ShowLevel = false,
            ShowXp = false,
            ShowPlaytime = false,
            ShowSessions = false,
            ShowStatus = false
        };
        full.SetFriend(friend with { PublicCard = privateCard });
        Check(((TextBlock)full.FindName("UsernameText")).Visibility == Visibility.Collapsed, "Hidden username must remain hidden on the full friend profile.");
        Check(((Border)full.FindName("LevelXpCard")).Visibility == Visibility.Collapsed, "Hidden level and XP must remove the full-profile stat card.");
        Check(((Border)full.FindName("PlayTimeCard")).Visibility == Visibility.Collapsed, "Hidden play time must remove the full-profile stat card.");
        Check(((Border)full.FindName("SessionsCard")).Visibility == Visibility.Collapsed, "Hidden sessions must remove the full-profile stat card.");
        Check(((TextBlock)full.FindName("StatusText")).Visibility == Visibility.Collapsed, "Hidden status must not leave an empty profile row.");

        full.SetFriend(friend, isSelf: true);
        Check(((Button)full.FindName("MessageButton")).Visibility == Visibility.Collapsed, "Own public-card preview must never offer messaging yourself.");
        Check(((TextBlock)full.FindName("PreviewText")).Visibility == Visibility.Visible, "Own card must be clearly identified as a public preview.");
        Check(((TextBlock)full.FindName("FriendsSinceText")).Text == "Public preview", "Own card preview must not invent a friendship date.");
        full.SetActivity(Array.Empty<GrevDadActivityEvent>());
        Check(((TextBlock)full.FindName("NoActivityText")).Visibility == Visibility.Collapsed, "Own preview must not show a fake empty friend-activity feed.");

        var avatarBorder = new Border { Width = 96, Height = 96 };
        ProfileAvatarShapeStyle.Apply(avatarBorder, "circle", 96);
        Check(avatarBorder.CornerRadius.TopLeft == 48 && avatarBorder.Clip is not null, "Circle avatar setting must clip the actual avatar content, not only round the border.");
        ProfileAvatarShapeStyle.Apply(avatarBorder, "square", 96);
        Check(avatarBorder.CornerRadius.TopLeft == 0, "Square avatar setting must restore square presentation.");

        Console.WriteLine("Profile social tests passed: friend cards, public visibility, stats, previews and avatar shape are consistent.");
        app.Shutdown();
    }

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in TextBlocks(child)) yield return nested;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
