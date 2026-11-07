using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GrevHome.Presentation;

namespace GrevHome.Views;

/// <summary>
/// Theme-driven arrangement of Home. The tiles, their handlers and their data never change with
/// the layout; this file only decides where sections go (stacked rows, one merged row, tabs or
/// blades), how tiles flow (row or grid), their size and shape, and the optional focused-title
/// line and bottom dock. The active layout arrives from <see cref="ThemeApplier.LayoutApplied"/>,
/// so a theme preview in the Theme Creator and a saved theme behave identically.
/// </summary>
public partial class DashboardView
{
    private const double DockTileSize = 64;
    private const string SectionTagPrefix = "section:";

    private ThemeLayout _layout = ThemeLayout.Classic;
    private bool _layoutInitialized;
    private bool _recentAvailable;
    private bool _friendsAvailable;
    private string _selectedSection = "apps";
    private readonly Dictionary<Button, string> _tileTitles = new();
    private readonly Dictionary<FrameworkElement, Thickness> _originalMargins = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    private sealed record HomeSection(string Id, string Title, FrameworkElement Block, FrameworkElement Heading, ScrollViewer Carousel);

    private IReadOnlyList<HomeSection> Sections =>
    [
        new("recent", "Recent", ActivitySection, ActivityHeading, RecentCarousel),
        new("account", "Account", AccountSection, AccountHeading, AccountCarousel),
        new("apps", "Apps", AppsSection, AppsHeading, AppsCarousel),
        new("friends", "Friends", FriendsSection, FriendsHeading, FriendsCarousel),
        new("system", "System", SystemSection, SystemHeading, SystemCarousel)
    ];

    private void InitializeLayout()
    {
        foreach (var section in Sections)
        {
            _originalMargins[section.Block] = section.Block.Margin;
            _originalMargins[section.Carousel] = section.Carousel.Margin;
        }
        _clockTimer.Tick += (_, _) => UpdateClock();
        ThemeApplier.LayoutApplied += layout => Dispatcher.Invoke(() => ApplyLayout(layout));
        ApplyLayout(ThemeApplier.CurrentLayout);
    }

    public ThemeLayout CurrentLayout => _layout;

    public void ApplyLayout(ThemeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_layoutInitialized && layout == _layout)
        {
            // Same layout, new colours: only the tab/blade highlight is drawn from the accent.
            UpdateSectionNavigationState();
            return;
        }
        _layoutInitialized = true;
        _layout = layout;

        Resources["DashboardTileCornerRadius"] = new CornerRadius(layout.EffectiveCornerRadius);
        DockHost.Resources["DashboardTileCornerRadius"] = new CornerRadius(DockTileSize / 2);
        WelcomeHeader.Visibility = layout.ShowWelcome ? Visibility.Visible : Visibility.Collapsed;
        FocusedTitleText.Visibility = layout.FocusedTitleVisible ? Visibility.Visible : Visibility.Collapsed;

        // System tiles move into the bottom dock (Switch/Wii) or back into the sections.
        if (layout.SystemDock && SystemSection.Parent == SectionsHost)
        {
            SectionsHost.Children.Remove(SystemSection);
            DockHost.Children.Add(SystemSection);
        }
        else if (!layout.SystemDock && SystemSection.Parent == DockHost)
        {
            DockHost.Children.Remove(SystemSection);
            SectionsHost.Children.Add(SystemSection);
        }
        DockBar.Visibility = layout.SystemDock || layout.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        DockClockText.Visibility = layout.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        if (layout.ShowClock) { UpdateClock(); _clockTimer.Start(); } else _clockTimer.Stop();

        // Merged: every section continues one long horizontal row inside SectionsScroll.
        var merged = layout.SectionMode == HomeSectionMode.Merged;
        SectionsHost.Orientation = merged ? Orientation.Horizontal : Orientation.Vertical;
        SectionsScroll.HorizontalScrollBarVisibility = merged ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        SectionsScroll.OpacityMask = null;

        foreach (var section in Sections)
        {
            var docked = layout.SystemDock && section.Id == "system";
            // A carousel scrolls itself only when it is a standalone row. Inside the merged row the
            // outer SectionsScroll scrolls instead, and in grid flow the tiles wrap.
            var scrollsItself = !merged && !docked && (layout.ItemFlow == HomeItemFlow.Row || section.Id == "recent");
            section.Carousel.HorizontalScrollBarVisibility = scrollsItself ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
            if (!scrollsItself) section.Carousel.OpacityMask = null;
            section.Block.Margin = merged || docked ? new Thickness(0) : _originalMargins[section.Block];
            section.Carousel.Margin = merged || docked ? new Thickness(0) : _originalMargins[section.Carousel];
        }

        // Centre/bottom placement needs a fixed height, so it applies to row layouts that never
        // need to scroll vertically; stacked or grid layouts always start at the top and scroll.
        var positioned = layout.ItemFlow == HomeItemFlow.Row && layout.SectionMode != HomeSectionMode.Stacked &&
                         layout.ContentPosition != HomeContentPosition.Top;
        DashboardScroll.VerticalScrollBarVisibility = positioned ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        SectionsScroll.VerticalAlignment = !positioned
            ? VerticalAlignment.Top
            : layout.ContentPosition == HomeContentPosition.Center ? VerticalAlignment.Center : VerticalAlignment.Bottom;

        RenderDashboardTiles();
        if (_lastSnapshot is { } snapshot) SetDashboardData(snapshot);
        foreach (var friend in FriendsPanel.Children.OfType<Button>()) ApplyTileMetrics(friend, keepSize: true);
        RefreshSections();
    }

    /// <summary>Shows or hides sections and rebuilds tabs/blades after availability changes.</summary>
    private void RefreshSections()
    {
        var available = Sections.Where(IsAvailable).ToList();
        var headingsVisible = _layout.ShowSectionHeadings && _layout.SectionMode == HomeSectionMode.Stacked;
        foreach (var section in Sections)
        {
            var docked = _layout.SystemDock && section.Id == "system";
            section.Heading.Visibility = headingsVisible && !docked ? Visibility.Visible : Visibility.Collapsed;
        }
        ActivitySummaryText.Visibility = headingsVisible ? Visibility.Visible : Visibility.Collapsed;

        var oneAtATime = _layout.SectionMode is HomeSectionMode.Tabs or HomeSectionMode.Blades;
        if (oneAtATime && available.Count > 0 && available.All(section => section.Id != _selectedSection))
        {
            _selectedSection = available.Any(section => section.Id == "apps") ? "apps" : available[0].Id;
        }

        foreach (var section in Sections)
        {
            var docked = _layout.SystemDock && section.Id == "system";
            var show = docked || (available.Contains(section) && (!oneAtATime || section.Id == _selectedSection));
            section.Block.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        TabBarPanel.Visibility = _layout.SectionMode == HomeSectionMode.Tabs ? Visibility.Visible : Visibility.Collapsed;
        BladePanel.Visibility = _layout.SectionMode == HomeSectionMode.Blades ? Visibility.Visible : Visibility.Collapsed;
        if (oneAtATime) BuildSectionNavigation(available);
        else
        {
            TabBarPanel.Children.Clear();
            BladePanel.Children.Clear();
        }
    }

    private bool IsAvailable(HomeSection section) => section.Id switch
    {
        "recent" => _recentAvailable,
        "friends" => _friendsAvailable,
        "system" => !_layout.SystemDock,
        _ => true
    };

    private void BuildSectionNavigation(IReadOnlyList<HomeSection> available)
    {
        // Rebuilding while one of these buttons has focus would drop focus, so an unchanged set of
        // sections only has its selected state refreshed.
        var host = _layout.SectionMode == HomeSectionMode.Tabs ? (Panel)TabBarPanel : BladePanel;
        var other = host == TabBarPanel ? (Panel)BladePanel : TabBarPanel;
        other.Children.Clear();
        var ids = available.Select(section => SectionTagPrefix + section.Id).ToArray();
        var existing = host.Children.OfType<Button>().Select(button => button.Tag as string).ToArray();
        if (!ids.SequenceEqual(existing))
        {
            host.Children.Clear();
            foreach (var section in available)
            {
                var button = _layout.SectionMode == HomeSectionMode.Tabs ? CreateTab(section) : CreateBlade(section);
                button.Click += (_, _) => FocusFirstTile(section);
                host.Children.Add(button);
            }
        }
        UpdateSectionNavigationState();
    }

    private Button CreateTab(HomeSection section)
    {
        var button = new Button
        {
            Tag = SectionTagPrefix + section.Id,
            Content = section.Title,
            FontSize = 26,
            Padding = new Thickness(18, 6, 18, 8),
            Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(0, 0, 0, 3),
            Background = Brushes.Transparent
        };
        button.SetResourceReference(ForegroundProperty, ThemeApplier.TextKey);
        return button;
    }

    private Button CreateBlade(HomeSection section)
    {
        var label = new TextBlock
        {
            Text = section.Title.ToUpperInvariant(),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            LayoutTransform = new RotateTransform(-90),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeApplier.ButtonTextKey);
        return new Button
        {
            Tag = SectionTagPrefix + section.Id,
            Content = label,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Bottom,
            BorderThickness = new Thickness(0)
        };
    }

    /// <summary>Selected tab is underlined; selected blade is wide and solid, others are slim and dimmer.</summary>
    private void UpdateSectionNavigationState()
    {
        var accent = TryFindResource(ThemeApplier.AccentKey) is SolidColorBrush brush ? brush.Color : Colors.SteelBlue;
        foreach (var button in TabBarPanel.Children.OfType<Button>())
        {
            var selected = button.Tag as string == SectionTagPrefix + _selectedSection;
            button.BorderBrush = selected ? new SolidColorBrush(accent) : Brushes.Transparent;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Light;
        }

        var index = 0;
        foreach (var button in BladePanel.Children.OfType<Button>())
        {
            var selected = button.Tag as string == SectionTagPrefix + _selectedSection;
            button.Width = selected ? 110 : 56;
            var alpha = (byte)(selected ? 255 : Math.Max(90, 200 - index * 25));
            button.Background = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
            index++;
        }
    }

    private void SelectSection(string id)
    {
        if (_selectedSection == id) return;
        _selectedSection = id;
        foreach (var section in Sections)
        {
            var docked = _layout.SystemDock && section.Id == "system";
            if (docked || !IsAvailable(section)) continue;
            section.Block.Visibility = section.Id == id ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateSectionNavigationState();
    }

    private void FocusFirstTile(HomeSection section)
    {
        SelectSection(section.Id);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var first = FindVisualChildren<Button>(section.Block).FirstOrDefault(button => button.IsVisible && button.IsEnabled);
            first?.Focus();
        }), DispatcherPriority.Input);
    }

    /// <summary>Called from the dashboard focus handler for every focused button on Home.</summary>
    private void OnLayoutFocus(Button button)
    {
        if (button.Tag is string tag && tag.StartsWith(SectionTagPrefix, StringComparison.Ordinal))
        {
            SelectSection(tag[SectionTagPrefix.Length..]);
            return;
        }

        var title = _tileTitles.TryGetValue(button, out var known) ? known : TitleFromToolTip(button);
        if (_layout.FocusedTitleVisible) FocusedTitleText.Text = title;
        DockLabelText.Text = SystemSection.Parent == DockHost && FindAncestor<Panel>(button) == SystemCarouselPanel ? title : string.Empty;
    }

    private static string TitleFromToolTip(Button button) =>
        button.ToolTip is string tip ? tip.Split(" • ")[0] : string.Empty;

    private bool IsDocked(Button button) => _layout.SystemDock && button.Parent == SystemCarouselPanel;

    /// <summary>Size and spacing for one tile from the active layout.</summary>
    private void ApplyTileMetrics(Button button, bool keepSize = false)
    {
        if (IsDocked(button))
        {
            button.Width = DockTileSize;
            button.Height = DockTileSize;
            button.Margin = new Thickness(8);
            return;
        }

        if (!keepSize)
        {
            var (width, height) = _layout.TileSize;
            button.Width = width;
            button.Height = height;
        }
        button.Margin = new Thickness(_layout.TileSpacing);
    }

    /// <summary>
    /// Artwork for a tile at the current layout: icon only when docked or when tile labels are
    /// off, full-bleed media when the tile has its own image, otherwise icon plus name.
    /// </summary>
    private FrameworkElement CreateTileContent(Button button, string displayName, string? iconAsset, string? mediaPath, string? color)
    {
        _tileTitles[button] = displayName;
        if (IsDocked(button)) return AppArtworkFactory.Create(iconAsset, color, DockTileSize, DockTileSize, 0);
        var (width, height) = _layout.TileSize;
        if (!string.IsNullOrWhiteSpace(mediaPath)) return AppArtworkFactory.CreateFullTile(mediaPath, color, width, height);
        if (!_layout.ShowTileLabels) return AppArtworkFactory.Create(iconAsset, color, width, height, 0);
        return AppArtworkFactory.CreateTile(displayName, iconAsset, color);
    }

    private void UpdateClock() => DockClockText.Text = DateTime.Now.ToString("t");

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
