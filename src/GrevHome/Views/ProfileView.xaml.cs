using System.Windows;
using System.Windows.Controls;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileView : UserControl
{
    public event EventHandler? EditProfileRequested;

    public ProfileView()
    {
        InitializeComponent();
    }

    public void SetProfile(LocalProfile? profile, string? sessionStatus = null, bool canEdit = true)
    {
        if (profile is null)
        {
            AvatarImage.Source = null;
            AvatarImage.Visibility = Visibility.Collapsed;
            AvatarText.Visibility = Visibility.Visible;
            AvatarText.Text = "?";
            DisplayNameText.Text = "No local profile";
            UsernameText.Text = "No local profile is available.";
            BioText.Text = string.Empty;
            RoleText.Text = string.Empty;
            SessionText.Text = string.Empty;
            GrevIdText.Text = "—";
            CreatedText.Text = "—";
            RoleDescriptionText.Text = "—";
            PermissionsText.Text = "—";
            EditProfileButton.IsEnabled = false;
            ShowStatsLoading("No profile activity available.");
            return;
        }

        AvatarImage.Source = ProfileAvatarCatalog.TryLoadCustomImage(profile);
        AvatarImage.Visibility = AvatarImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        AvatarText.Visibility = AvatarImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        AvatarText.Text = ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName);
        DisplayNameText.Text = profile.DisplayName;
        UsernameText.Text = $"@{profile.Username}";
        BioText.Text = string.IsNullOrWhiteSpace(profile.Bio)
            ? "No About text yet. Edit Profile to add one."
            : profile.Bio;
        RoleText.Text = profile.Role.ToString().ToUpperInvariant();
        SessionText.Text = sessionStatus ?? "Not currently signed in";
        GrevIdText.Text = profile.GrevId;
        CreatedText.Text = profile.CreatedAtUtc.ToLocalTime().ToString("d MMM yyyy");
        RoleDescriptionText.Text = AccountAuthorizationService.DescribeRole(profile.Role);
        PermissionsText.Text = AccountAuthorizationService.SummarizePermissions(profile.Role);
        EditProfileButton.IsEnabled = canEdit;
        ShowStatsLoading("Reading Grev Home activity…");
    }

    public void SetStats(ProfileStatsSnapshot stats)
    {
        LevelText.Text = $"LEVEL {stats.Progression.Level}";
        LevelNumberText.Text = stats.Progression.Level.ToString();
        LevelProgressBar.Value = stats.Progression.ProgressPercent;
        XpText.Text = $"{stats.Progression.XpIntoLevel:N0} / {stats.Progression.XpRequiredForNextLevel:N0} XP to next level  •  {stats.Progression.TotalXp:N0} total XP";
        TotalTimeText.Text = FormatDuration(stats.TotalTrackedSeconds);
        SessionsText.Text = stats.CompletedSessions.ToString("N0");
        AppsPlayedText.Text = stats.UniqueApps.ToString("N0");
        ActiveSessionsText.Text = stats.ActiveSessions.ToString("N0");

        LastActivityText.Text = stats.LastActivityAtUtc.HasValue
            ? stats.LastActivityAtUtc.Value.ToLocalTime().ToString("g")
            : "No tracked activity yet";
        ActivityStatusText.Text = stats.ActiveSessions > 0
            ? $"{stats.ActiveSessions} managed app{(stats.ActiveSessions == 1 ? string.Empty : "s")} currently contributing live tracked time."
            : "Completed Grev Home sessions are included in the profile totals.";

        TopAppsPanel.Children.Clear();
        NoTopAppsText.Visibility = stats.TopApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var app in stats.TopApps)
        {
            var row = new Border
            {
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 4, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(9, 12, 18)),
                CornerRadius = new CornerRadius(9)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock
            {
                Text = app.IsRunning ? $"{app.AppName}  •  RUNNING" : app.AppName,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var time = new TextBlock
            {
                Text = FormatDuration(app.TotalSeconds),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(time, 1);
            grid.Children.Add(name);
            grid.Children.Add(time);
            row.Child = grid;
            TopAppsPanel.Children.Add(row);
        }

        SourcesPanel.Children.Clear();
        foreach (var source in stats.Sources)
        {
            var card = new Border
            {
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(9, 12, 18)),
                CornerRadius = new CornerRadius(9)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"{source.DisplayName}  •  {(source.IsConnected ? "CONNECTED" : "UNAVAILABLE")}",
                FontWeight = FontWeights.SemiBold,
                Foreground = source.IsConnected
                    ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                    : (System.Windows.Media.Brush)FindResource("MutedBrush")
            });
            stack.Children.Add(new TextBlock
            {
                Text = source.IsConnected
                    ? $"{FormatDuration(source.TotalSeconds)}  •  {source.CompletedSessions:N0} sessions  •  {source.UniqueApps:N0} apps"
                    : source.Status,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            if (source.IsConnected)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = source.Status,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 11,
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            card.Child = stack;
            SourcesPanel.Children.Add(card);
        }
    }

    public void ShowStatsError(string message) => ShowStatsLoading($"Profile activity unavailable: {message}");

    private void ShowStatsLoading(string message)
    {
        LevelText.Text = "LEVEL —";
        LevelNumberText.Text = "—";
        LevelProgressBar.Value = 0;
        XpText.Text = message;
        TotalTimeText.Text = "—";
        SessionsText.Text = "—";
        AppsPlayedText.Text = "—";
        ActiveSessionsText.Text = "—";
        LastActivityText.Text = message;
        ActivityStatusText.Text = string.Empty;
        TopAppsPanel.Children.Clear();
        NoTopAppsText.Visibility = Visibility.Visible;
        SourcesPanel.Children.Clear();
    }

    private static string FormatDuration(long seconds)
    {
        seconds = Math.Max(0, seconds);
        var duration = TimeSpan.FromSeconds(seconds);
        if (duration.TotalHours >= 100) return $"{duration.TotalHours:0} h";
        if (duration.TotalHours >= 1) return $"{duration.TotalHours:0.0} h";
        if (duration.TotalMinutes >= 1) return $"{duration.TotalMinutes:0} min";
        return $"{duration.TotalSeconds:0} sec";
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e) => EditProfileRequested?.Invoke(this, EventArgs.Empty);
}
