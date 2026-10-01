namespace GrevHome.Presentation;

/// <summary>How Home's sections (Recent, Account, Apps, Friends, System) are presented.</summary>
public enum HomeSectionMode
{
    /// <summary>Labelled rows stacked top to bottom (the original Grev Home layout).</summary>
    Stacked,
    /// <summary>Every section joined into one continuous row with no headings (PS4/PS5, Switch).</summary>
    Merged,
    /// <summary>A tab strip across the top; one section shows at a time (Xbox 360 NXE tabs).</summary>
    Tabs,
    /// <summary>Tall vertical blades down the side; one section shows at a time (Xbox 360 blades).</summary>
    Blades
}

/// <summary>How the tiles inside a section flow.</summary>
public enum HomeItemFlow
{
    /// <summary>One horizontally scrolling row.</summary>
    Row,
    /// <summary>A grid that wraps onto new lines and scrolls vertically (Xbox One, Wii).</summary>
    Grid
}

public enum HomeTileShape
{
    /// <summary>The original 285 × 145 landscape tile.</summary>
    Wide,
    Square,
    /// <summary>A rounded 4:3 "channel" (Wii).</summary>
    Channel,
    Circle
}

public enum HomeContentPosition
{
    Top,
    Center,
    Bottom
}

public enum HomeFocusEffect
{
    /// <summary>Rise a little and glow in the accent colour (Grev Home's original motion).</summary>
    LiftAndGlow,
    /// <summary>Grow noticeably larger, PS5 style.</summary>
    Grow,
    /// <summary>Only the accent outline; tiles never move.</summary>
    Outline
}

public enum HomeBackgroundStyle
{
    Solid,
    /// <summary>A soft radial glow from the top left (Grev Home's original background).</summary>
    Glow,
    /// <summary>A vertical gradient from the glow colour down to the window background.</summary>
    Gradient
}

/// <summary>
/// The layout half of a theme: where Home's tiles go and what they look like. Colours stay on
/// <see cref="ThemeDefinition"/>. A theme saved before layouts existed has no layout and uses
/// <see cref="Classic"/>, which reproduces the original Home exactly.
/// </summary>
public sealed record ThemeLayout(
    HomeSectionMode SectionMode = HomeSectionMode.Stacked,
    HomeItemFlow ItemFlow = HomeItemFlow.Row,
    HomeTileShape TileShape = HomeTileShape.Wide,
    int TileScalePercent = 100,
    int CornerRadius = 0,
    int TileSpacing = 8,
    HomeContentPosition ContentPosition = HomeContentPosition.Top,
    HomeFocusEffect FocusEffect = HomeFocusEffect.LiftAndGlow,
    bool ShowTileLabels = true,
    bool ShowSectionHeadings = true,
    bool ShowWelcome = true,
    bool ShowFocusedTitle = false,
    bool SystemDock = false,
    bool ShowClock = false,
    string FontFamily = "Segoe UI",
    HomeBackgroundStyle BackgroundStyle = HomeBackgroundStyle.Glow,
    string BackgroundGlow = "#182137")
{
    public const int MinimumTileScale = 60;
    public const int MaximumTileScale = 160;
    public const int MaximumCornerRadius = 60;
    public const int MaximumTileSpacing = 40;

    /// <summary>Fonts that ship with every supported Windows installation.</summary>
    public static IReadOnlyList<string> Fonts { get; } =
        ["Segoe UI", "Segoe UI Light", "Segoe UI Semibold", "Bahnschrift", "Trebuchet MS", "Verdana", "Georgia"];

    public static ThemeLayout Classic { get; } = new();

    public void Validate()
    {
        if (TileScalePercent is < MinimumTileScale or > MaximumTileScale)
            throw new InvalidOperationException($"Tile size must be between {MinimumTileScale}% and {MaximumTileScale}%.");
        if (CornerRadius is < 0 or > MaximumCornerRadius)
            throw new InvalidOperationException($"Tile corners must be between 0 and {MaximumCornerRadius}.");
        if (TileSpacing is < 0 or > MaximumTileSpacing)
            throw new InvalidOperationException($"Tile spacing must be between 0 and {MaximumTileSpacing}.");
        if (!Fonts.Contains(FontFamily, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Choose one of the supported fonts: {string.Join(", ", Fonts)}.");
        if (!Enum.IsDefined(SectionMode) || !Enum.IsDefined(ItemFlow) || !Enum.IsDefined(TileShape) ||
            !Enum.IsDefined(ContentPosition) || !Enum.IsDefined(FocusEffect) || !Enum.IsDefined(BackgroundStyle))
            throw new InvalidOperationException("This theme's layout uses an option this version of Grev Home does not know.");
        ThemeDefinition.CheckColor(BackgroundGlow, "Background glow");
    }

    /// <summary>The tile's size before scaling, by shape.</summary>
    public (double Width, double Height) BaseTileSize => TileShape switch
    {
        HomeTileShape.Square => (170, 170),
        HomeTileShape.Channel => (220, 165),
        HomeTileShape.Circle => (150, 150),
        _ => (285, 145)
    };

    /// <summary>The tile's on-screen size.</summary>
    public (double Width, double Height) TileSize
    {
        get
        {
            var (width, height) = BaseTileSize;
            var scale = Math.Clamp(TileScalePercent, MinimumTileScale, MaximumTileScale) / 100d;
            return (Math.Round(width * scale), Math.Round(height * scale));
        }
    }

    /// <summary>Corner radius actually drawn: a circle is always fully round.</summary>
    public double EffectiveCornerRadius
    {
        get
        {
            var (width, height) = TileSize;
            return TileShape == HomeTileShape.Circle
                ? Math.Min(width, height) / 2
                : Math.Min(CornerRadius, Math.Min(width, height) / 2);
        }
    }

    /// <summary>
    /// Labels are always shown somewhere: with tile labels off, the focused tile's name is shown
    /// as a title instead, so nothing on Home becomes anonymous.
    /// </summary>
    public bool FocusedTitleVisible => ShowFocusedTitle || !ShowTileLabels;

    /// <summary>Scale a focused tile grows to.</summary>
    public double FocusScale => FocusEffect switch
    {
        HomeFocusEffect.Grow => 1.12,
        HomeFocusEffect.Outline => 1,
        _ => 1.045
    };
}
