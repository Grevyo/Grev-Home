using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileView : UserControl
{
    private ProfilePresentationSettings _presentation = ProfilePresentationSettings.Default;
    private ProfileStatsSnapshot? _lastStats;
    private SolidColorBrush _levelBandBrush = new(Color.FromRgb(125, 137, 156));
    private string? _currentGrevId;

    public event EventHandler? EditProfileRequested;

    public ProfileView()
    {
        InitializeComponent();
        ApplyPresentation(ProfilePresentationSettings.Default);
    }

    public void SetProfile(LocalProfile? profile, string? sessionStatus = null, bool canEdit = true)
    {
        _currentGrevId = profile?.GrevId;
        SetCloudAccountData(null,false);
        _lastStats = null;
        ApplyPresentation(ProfilePresentationSettings.Default);

        if (profile is null)
        {
            AvatarImage.Source = null;
            AvatarImage.Visibility = Visibility.Collapsed;
            AvatarText.Visibility = Visibility.Visible;
            AvatarText.Text = "?";
            DisplayNameText.Text = "No local profile";
            UsernameText.Text = "No local profile is available.";
            StatusMessageText.Text = string.Empty;
            BioText.Text = string.Empty;
            RoleText.Text = string.Empty;
            SessionText.Text = string.Empty;
            GrevIdText.Text = "—";
            CreatedText.Text = "—";
            RoleDescriptionText.Text = "—";
            PermissionsText.Text = "—";
            EditProfileButton.IsEnabled = false;
            ApplyRolePresentation(AccountRole.Guest);
            ShowStatsLoading("No profile activity available.");
            return;
        }

        AvatarImage.Source = ProfileAvatarCatalog.TryLoadCustomImage(profile);
        AvatarImage.Visibility = AvatarImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        AvatarText.Visibility = AvatarImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        AvatarText.Text = ProfileAvatarCatalog.GetDisplayGlyph(profile.AvatarKey, profile.DisplayName);
        DisplayNameText.Text = profile.DisplayName;
        UsernameText.Text = $"@{profile.Username}";
        StatusMessageText.Text = string.IsNullOrWhiteSpace(profile.StatusMessage)
            ? "Set a status or tagline from Edit Profile."
            : profile.StatusMessage;
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
        ApplyRolePresentation(profile.Role);
        ShowStatsLoading("Reading Grev Home activity…");
    }

    public void SetPresentation(ProfilePresentationSettings settings)
    {
        _presentation = settings;
        ApplyPresentation(settings);
        if (_lastStats is not null)
        {
            RenderShowcase(_lastStats);
        }
    }

    private void ApplyPresentation(ProfilePresentationSettings settings)
    {
        _presentation = settings;
        var normalizedBanner = ProfileBannerCatalog.Normalize(settings.BannerKey);
        ProfileBannerGrid.Background = ProfileBannerCatalog.CreateBrush(normalizedBanner);
        ProfileBannerImage.Source = null;
        ProfileBannerImage.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(_currentGrevId) &&
            string.Equals(normalizedBanner, ProfileBannerCatalog.CustomKey, StringComparison.OrdinalIgnoreCase))
        {
            var source = ProfileBannerCatalog.TryLoadCustomImage(_currentGrevId, settings);
            if (source is not null)
            {
                ProfileBannerImage.Source = source;
                ProfileBannerImage.Visibility = Visibility.Visible;
            }
        }

        BannerLabelText.Text = string.Equals(normalizedBanner, ProfileBannerCatalog.CustomKey, StringComparison.OrdinalIgnoreCase)
            ? "CUSTOM PROFILE BANNER"
            : $"{ProfileBannerCatalog.Presets.First(preset => preset.Key == normalizedBanner).Name.ToUpperInvariant()} PROFILE BANNER";

        ShowcaseModeText.Text = settings.ShowcaseMode switch
        {
            ProfileShowcaseMode.RecentActivity => "RECENT ACTIVITY",
            ProfileShowcaseMode.Milestones => "MILESTONES",
            _ => "TOP PLAYED"
        };
    }

    private void ApplyRolePresentation(AccountRole role)
    {
        var roleBrush = (SolidColorBrush)FindResource(role switch
        {
            AccountRole.Admin => "AdminRoleBrush",
            AccountRole.Standard => "StandardRoleBrush",
            _ => "GuestRoleBrush"
        });

        ProfileHeaderCard.BorderBrush = roleBrush;
        ProfileAvatarBorder.BorderBrush = roleBrush;
        RoleText.Foreground = roleBrush;
        ProfileHeaderCard.Effect = role switch
        {
            AccountRole.Admin => new DropShadowEffect
            {
                Color = roleBrush.Color,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.5
            },
            AccountRole.Standard => new DropShadowEffect
            {
                Color = roleBrush.Color,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.18
            },
            _ => null
        };
    }

    public void SetStats(ProfileStatsSnapshot stats)
    {
        _lastStats = stats;
        _levelBandBrush = GetLevelBandBrush(stats.Progression.Level);
        LevelText.Text = $"LEVEL {stats.Progression.Level}";
        LevelNumberText.Text = stats.Progression.Level.ToString();
        LevelText.Foreground = _levelBandBrush;
        LevelNumberText.Foreground = _levelBandBrush;
        LevelProgressBar.Foreground = _levelBandBrush;
        LevelBadgeBorder.BorderBrush = _levelBandBrush;
        BannerTierStrip.Fill = _levelBandBrush;
        LevelProgressBar.Value = stats.Progression.ProgressPercent;
        XpText.Text = $"{stats.Progression.XpIntoLevel:N0} / {stats.Progression.XpRequiredForNextLevel:N0} XP to next level  •  {stats.Progression.TotalXp:N0} total XP";
        TotalTimeText.Text = FormatDuration(stats.TotalTrackedSeconds);
        SessionsText.Text = stats.CompletedSessions.ToString("N0");
        AppsPlayedText.Text = stats.UniqueApps.ToString("N0");
        ActiveSessionsText.Text = stats.ActiveSessions.ToString("N0");

        LastActivityText.Text = stats.LastActivityAtUtc.HasValue
            ? FormatRelativeTime(stats.LastActivityAtUtc.Value)
            : "No tracked activity yet";
        ActivityStatusText.Text = stats.ActiveSessions > 0
            ? $"{stats.ActiveSessions} managed app{(stats.ActiveSessions == 1 ? string.Empty : "s")} currently contributing live tracked time."
            : "Completed Grev Home sessions are included in the profile totals.";

        TopAppsPanel.Children.Clear();
        NoTopAppsText.Visibility = stats.TopApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var app in stats.TopApps)
        {
            TopAppsPanel.Children.Add(CreateAppRow(
                app.IsRunning ? $"{app.AppName}  •  RUNNING" : app.AppName,
                FormatDuration(app.TotalSeconds)));
        }

        RecentActivityPanel.Children.Clear();
        NoRecentActivityText.Visibility = stats.RecentActivity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var activity in stats.RecentActivity)
        {
            var detail = activity.IsRunning
                ? $"RUNNING NOW  •  {FormatDuration(activity.TotalSeconds)}"
                : $"{FormatRelativeTime(activity.LastActivityAtUtc)}  •  {FormatDuration(activity.TotalSeconds)} total  •  {activity.SessionCount:N0} sessions";
            RecentActivityPanel.Children.Add(CreateActivityRow(activity.AppName, detail));
        }

        MilestonesPanel.Children.Clear();
        var earnedCount = stats.Milestones.Count(milestone => milestone.IsEarned);
        MilestoneSummaryText.Text = $"{earnedCount} of {stats.Milestones.Count} earned from real Grev Home activity.";
        foreach (var milestone in stats.Milestones)
        {
            MilestonesPanel.Children.Add(CreateMilestoneCard(milestone));
        }

        SourcesPanel.Children.Clear();
        foreach (var source in stats.Sources)
        {
            SourcesPanel.Children.Add(CreateSourceCard(source));
        }

        RenderShowcase(stats);
    }

    private void RenderShowcase(ProfileStatsSnapshot stats)
    {
        ShowcasePanel.Children.Clear();
        ShowcaseEmptyText.Visibility = Visibility.Collapsed;

        switch (_presentation.ShowcaseMode)
        {
            case ProfileShowcaseMode.RecentActivity:
                ShowcaseTitleText.Text = "Recent Activity";
                ShowcaseSubtitleText.Text = "The latest things this profile has been doing in Grev Home.";
                foreach (var activity in stats.RecentActivity.Take(3))
                {
                    ShowcasePanel.Children.Add(CreateShowcaseCard(
                        activity.IsRunning ? "LIVE" : FormatRelativeTime(activity.LastActivityAtUtc).ToUpperInvariant(),
                        activity.AppName,
                        activity.IsRunning
                            ? $"Running now  •  {FormatDuration(activity.TotalSeconds)} tracked"
                            : $"{FormatDuration(activity.TotalSeconds)} total  •  {activity.SessionCount:N0} sessions"));
                }
                break;

            case ProfileShowcaseMode.Milestones:
                ShowcaseTitleText.Text = "Milestone Cabinet";
                ShowcaseSubtitleText.Text = "Achievements earned from actual Grev Home activity.";
                var milestones = stats.Milestones
                    .OrderByDescending(milestone => milestone.IsEarned)
                    .ThenByDescending(milestone => milestone.ProgressValue / (double)Math.Max(1, milestone.TargetValue))
                    .Take(3)
                    .ToArray();
                foreach (var milestone in milestones)
                {
                    ShowcasePanel.Children.Add(CreateShowcaseCard(
                        milestone.IsEarned ? "EARNED" : "IN PROGRESS",
                        milestone.Title,
                        milestone.IsEarned ? milestone.Description : milestone.ProgressLabel));
                }
                break;

            default:
                ShowcaseTitleText.Text = "Top Played";
                ShowcaseSubtitleText.Text = "The apps that define this Grev Home profile most.";
                var rank = 1;
                foreach (var app in stats.TopApps.Take(3))
                {
                    ShowcasePanel.Children.Add(CreateShowcaseCard(
                        $"#{rank++:00}",
                        app.AppName,
                        $"{FormatDuration(app.TotalSeconds)}  •  {app.SessionCount:N0} sessions{(app.IsRunning ? "  •  RUNNING" : string.Empty)}"));
                }
                break;
        }

        ShowcaseEmptyText.Visibility = ShowcasePanel.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement CreateShowcaseCard(string eyebrow, string title, string detail)
    {
        var card = new Border
        {
            MinHeight = 126,
            Padding = new Thickness(16),
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
            BorderBrush = _levelBandBrush,
            BorderThickness = new Thickness(1, 4, 1, 1),
            CornerRadius = new CornerRadius(0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = eyebrow,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = _levelBandBrush
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Margin = new Thickness(0, 7, 0, 0),
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        card.Child = stack;
        return card;
    }

    private UIElement CreateSourceCard(ProfileStatSourceSnapshot source)
    {
        var card = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"{source.DisplayName}  •  {(source.IsConnected ? "CONNECTED" : "UNAVAILABLE")}",
            FontWeight = FontWeights.SemiBold,
            Foreground = source.IsConnected
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = source.IsConnected
                ? $"{FormatDuration(source.TotalSeconds)}  •  {source.CompletedSessions:N0} sessions  •  {source.UniqueApps:N0} apps"
                : source.Status,
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (source.IsConnected)
        {
            stack.Children.Add(new TextBlock
            {
                Text = source.Status,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        card.Child = stack;
        return card;
    }

    public void ShowStatsError(string message) => ShowStatsLoading($"Profile activity unavailable: {message}");

    private UIElement CreateAppRow(string nameText, string detailText)
    {
        var row = new Border
        {
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = nameText,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var detail = new TextBlock
        {
            Text = detailText,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(detail, 1);
        grid.Children.Add(name);
        grid.Children.Add(detail);
        row.Child = grid;
        return row;
    }

    private UIElement CreateActivityRow(string appName, string detail)
    {
        var row = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = appName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        row.Child = stack;
        return row;
    }

    private UIElement CreateMilestoneCard(ProfileMilestoneStat milestone)
    {
        var card = new Border
        {
            Width = 210,
            MinHeight = 92,
            Padding = new Thickness(12),
            Margin = new Thickness(4),
            Background = new SolidColorBrush(
                milestone.IsEarned
                    ? Color.FromRgb(18, 29, 38)
                    : Color.FromRgb(9, 12, 18)),
            BorderBrush = milestone.IsEarned
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = milestone.IsEarned ? $"✓ {milestone.Title}" : $"○ {milestone.Title}",
            FontWeight = FontWeights.SemiBold,
            Foreground = milestone.IsEarned
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = milestone.Description,
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = milestone.IsEarned ? "EARNED" : milestone.ProgressLabel,
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedBrush")
        });
        card.Child = stack;
        return card;
    }

    private void ShowStatsLoading(string message)
    {
        LevelText.Text = "LEVEL —";
        LevelNumberText.Text = "—";
        _levelBandBrush = new SolidColorBrush(Color.FromRgb(125, 137, 156));
        LevelText.Foreground = _levelBandBrush;
        LevelNumberText.Foreground = _levelBandBrush;
        LevelProgressBar.Foreground = _levelBandBrush;
        LevelBadgeBorder.BorderBrush = _levelBandBrush;
        BannerTierStrip.Fill = _levelBandBrush;
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
        RecentActivityPanel.Children.Clear();
        NoRecentActivityText.Visibility = Visibility.Visible;
        MilestonesPanel.Children.Clear();
        MilestoneSummaryText.Text = message;
        SourcesPanel.Children.Clear();
        ShowcasePanel.Children.Clear();
        ShowcaseEmptyText.Visibility = Visibility.Visible;
        ShowcaseSubtitleText.Text = message;
    }

    private static SolidColorBrush GetLevelBandBrush(int level)
    {
        var band = Math.Max(0, (level - 1) / 10);
        var hue = (195d + band * 47d) % 360d;
        var color = ColorFromHsv(hue, 0.68d, 0.95d);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var hPrime = hue / 60d;
        var x = chroma * (1d - Math.Abs(hPrime % 2d - 1d));
        var (r1, g1, b1) = hPrime switch
        {
            >= 0d and < 1d => (chroma, x, 0d),
            >= 1d and < 2d => (x, chroma, 0d),
            >= 2d and < 3d => (0d, chroma, x),
            >= 3d and < 4d => (0d, x, chroma),
            >= 4d and < 5d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var match = value - chroma;
        return Color.FromRgb(
            (byte)Math.Round((r1 + match) * 255d),
            (byte)Math.Round((g1 + match) * 255d),
            (byte)Math.Round((b1 + match) * 255d));
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

    private static string FormatRelativeTime(DateTimeOffset timestampUtc)
    {
        var local = timestampUtc.ToLocalTime();
        var age = DateTimeOffset.Now - local;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalMinutes < 1) return "Just now";
        if (age.TotalHours < 1) return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
        if (age.TotalDays < 1) return $"{Math.Max(1, (int)age.TotalHours)} h ago";
        if (age.TotalDays < 7) return $"{Math.Max(1, (int)age.TotalDays)} d ago";
        return local.ToString("d MMM yyyy");
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e) => EditProfileRequested?.Invoke(this, EventArgs.Empty);
}
