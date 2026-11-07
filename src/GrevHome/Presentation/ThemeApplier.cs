using System.Windows.Media;

namespace GrevHome.Presentation;

/// <summary>
/// Applies a <see cref="ThemeDefinition"/> to the running application. Grev Home's shared styles
/// (the base Button style, SharpTileButtonStyle, ShellModalCardStyle, role badges, the window
/// background) reference these resource keys with DynamicResource rather than StaticResource
/// specifically so this can replace them at any time - including while the shell is already
/// running - and have every element that uses them re-color immediately, whether it is on screen
/// right now or drawn later. Nothing here mutates an existing brush's Color; each call swaps in a
/// fresh SolidColorBrush, which sidesteps any question of whether a previous brush was frozen.
/// </summary>
public static class ThemeApplier
{
    public const string WindowBackgroundKey = "WindowBackgroundBrush";
    public const string WindowBackgroundColorKey = "WindowBackgroundColor";
    public const string CardBackgroundKey = "CardBackgroundBrush";
    public const string CardBorderKey = "CardBorderBrush";
    public const string SurfaceKey = "SurfaceBrush";
    public const string SurfaceHoverKey = "SurfaceHoverBrush";
    public const string AccentKey = "AccentBrush";
    public const string MutedKey = "MutedBrush";
    public const string AdminRoleKey = "AdminRoleBrush";
    public const string StandardRoleKey = "StandardRoleBrush";
    public const string GuestRoleKey = "GuestRoleBrush";
    public const string TextKey = "TextBrush";
    public const string ButtonTextKey = "ButtonTextBrush";
    public const string FontFamilyKey = "ShellFontFamily";
    public const string ShellBackgroundKey = "ShellBackgroundBrush";
    public const string HeaderBackgroundKey = "HeaderBackgroundBrush";

    /// <summary>The layout of the theme applied most recently (Classic before any theme).</summary>
    public static ThemeLayout CurrentLayout { get; private set; } = ThemeLayout.Classic;

    /// <summary>
    /// Raised after a theme is applied, so surfaces whose arrangement comes from the theme
    /// (Home) can re-lay themselves out. Colours need no event: they are DynamicResources.
    /// </summary>
    public static event Action<ThemeLayout>? LayoutApplied;

    public static void Apply(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Validate();
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null) return;

        resources[WindowBackgroundKey] = Brush(theme.WindowBackground);
        // Kept alongside the brush for the one spot - the window's own ambient background
        // gradient - that needs a raw Color rather than a Brush.
        resources[WindowBackgroundColorKey] = ParseColor(theme.WindowBackground);
        resources[CardBackgroundKey] = Brush(theme.CardBackground);
        resources[CardBorderKey] = Brush(theme.CardBorder);
        resources[SurfaceKey] = Brush(theme.Surface);
        resources[SurfaceHoverKey] = Brush(theme.SurfaceHover);
        resources[AccentKey] = Brush(theme.Accent);
        resources[MutedKey] = Brush(theme.Muted);
        resources[AdminRoleKey] = Brush(theme.AdminRole);
        resources[StandardRoleKey] = Brush(theme.StandardRole);
        resources[GuestRoleKey] = Brush(theme.GuestRole);
        resources[TextKey] = Brush(theme.EffectiveText);
        resources[ButtonTextKey] = Brush(theme.EffectiveButtonText);
        // The persistent header is the card colour, slightly see-through, so it reads on light
        // and dark themes alike.
        var header = ParseColor(theme.CardBackground);
        resources[HeaderBackgroundKey] = new SolidColorBrush(Color.FromArgb(0xD9, header.R, header.G, header.B));

        var layout = theme.EffectiveLayout;
        resources[FontFamilyKey] = new FontFamily(layout.FontFamily);
        resources[ShellBackgroundKey] = CreateShellBackground(theme.WindowBackground, layout);
        ShellTileMotion.ConfigureFocus(layout.FocusScale, ParseColor(theme.Accent), layout.FocusEffect != HomeFocusEffect.Outline);

        CurrentLayout = layout;
        LayoutApplied?.Invoke(layout);
    }

    /// <summary>The window's own background. Glow with the default colours is the original look.</summary>
    internal static Brush CreateShellBackground(string windowBackground, ThemeLayout layout)
    {
        var baseColor = ParseColor(windowBackground);
        var glow = ParseColor(layout.BackgroundGlow);
        Brush brush = layout.BackgroundStyle switch
        {
            HomeBackgroundStyle.Solid => new SolidColorBrush(baseColor),
            HomeBackgroundStyle.Gradient => new LinearGradientBrush(glow, baseColor, 90),
            _ => new RadialGradientBrush
            {
                Center = new System.Windows.Point(0.45, 0.25),
                GradientOrigin = new System.Windows.Point(0.45, 0.25),
                RadiusX = 0.9,
                RadiusY = 0.9,
                GradientStops = { new GradientStop(glow, 0), new GradientStop(baseColor, 1) }
            }
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(string hex) => new(ParseColor(hex));

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
