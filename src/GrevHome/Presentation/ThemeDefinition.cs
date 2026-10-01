using System.Text.RegularExpressions;

namespace GrevHome.Presentation;

/// <summary>
/// A complete Grev Home chrome palette. This is deliberately small: the handful of colors that
/// the shell's shared styles (the base Button style, SharpTileButtonStyle, ShellModalCardStyle,
/// role badges, the window background) already draw from. A theme does not touch per-app or
/// per-GrevID dashboard tile artwork/colors - those remain their own override layer, resolved as
/// documented in DASHBOARD_PRESENTATION.md (shipped fallback -> active theme -> GrevID override).
/// </summary>
public sealed record ThemeDefinition(
    string Id,
    string Name,
    string WindowBackground,
    string CardBackground,
    string CardBorder,
    string Surface,
    string SurfaceHover,
    string Accent,
    string Muted,
    string AdminRole,
    string StandardRole,
    string GuestRole,
    bool IsBuiltIn = false,
    string? Text = null,
    string? ButtonText = null,
    ThemeLayout? Layout = null)
{
    // Optional so every theme saved before these existed still loads and looks the same.
    public string EffectiveText => Text ?? "#FFFFFF";
    public string EffectiveButtonText => ButtonText ?? "#FFFFFF";
    public ThemeLayout EffectiveLayout => Layout ?? ThemeLayout.Classic;

    private static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Validates every color and the display name. Thrown messages are shown directly to the
    /// controller-first Theme Creator, so they name the field in plain language.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("A theme needs a name.");
        if (Name.Trim().Length > 60) throw new InvalidOperationException("Theme names must be 60 characters or fewer.");
        CheckColor(WindowBackground, "Window background");
        CheckColor(CardBackground, "Card background");
        CheckColor(CardBorder, "Card border");
        CheckColor(Surface, "Surface");
        CheckColor(SurfaceHover, "Surface hover");
        CheckColor(Accent, "Accent");
        CheckColor(Muted, "Muted text");
        CheckColor(AdminRole, "Admin role color");
        CheckColor(StandardRole, "Standard role color");
        CheckColor(GuestRole, "Guest role color");
        if (Text is not null) CheckColor(Text, "Text");
        if (ButtonText is not null) CheckColor(ButtonText, "Button text");
        Layout?.Validate();
    }

    internal static void CheckColor(string value, string field)
    {
        if (!HexPattern.IsMatch(value ?? string.Empty))
            throw new InvalidOperationException($"{field} must be a 6-digit hex color, like #7EA6FF.");
    }

    /// <summary>
    /// Checks the color pairs that actually carry text or a focus ring in the shell, using the
    /// same WCAG relative-luminance contrast ratio browsers use for accessibility checks. This is
    /// advisory, not a save-blocking rule: a theme with low contrast is still a valid theme, and a
    /// deliberately low-contrast/moody look is a legitimate choice. The Theme Creator surfaces
    /// these as a warning so a choice that will actually be hard to read - the two colors turning
    /// out identical, or a light accent on a light surface - isn't discovered by accident.
    /// </summary>
    public IReadOnlyList<string> GetContrastWarnings()
    {
        var warnings = new List<string>();
        void Check(string label, string foreground, string background)
        {
            var ratio = ContrastRatio(foreground, background);
            if (ratio < MinimumReadableContrast)
                warnings.Add($"{label} is hard to read: contrast ratio {ratio:0.0}:1 (aim for at least {MinimumReadableContrast:0.0}:1).");
        }

        // Button labels use ButtonText (white unless the theme says otherwise) on Surface and
        // Surface Hover; page text uses Text on the window and card backgrounds.
        Check("Button text on Surface", EffectiveButtonText, Surface);
        Check("Button text on Surface Hover", EffectiveButtonText, SurfaceHover);
        Check("Text on Window Background", EffectiveText, WindowBackground);
        Check("Text on Card Background", EffectiveText, CardBackground);
        Check("Muted text on Window Background", Muted, WindowBackground);
        Check("Muted text on Card Background", Muted, CardBackground);
        Check("Accent on Card Background", Accent, CardBackground);
        return warnings;
    }

    private const double MinimumReadableContrast = 3.0;

    private static double ContrastRatio(string foreground, string background)
    {
        var l1 = RelativeLuminance(foreground);
        var l2 = RelativeLuminance(background);
        var (lighter, darker) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        if (hex is null || !HexPattern.IsMatch(hex)) return 0;
        var r = Linearize(Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0);
        var g = Linearize(Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0);
        var b = Linearize(Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double Linearize(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}

/// <summary>
/// The themes shipped with Grev Home. "grev-default" reproduces the shell's original hardcoded
/// palette exactly, so installing this feature changes nothing for a machine that never opens the
/// Theme Creator. The others exist to prove the engine actually re-colors the whole shell rather
/// than just Dashboard tiles.
/// </summary>
public static class ThemeCatalog
{
    public const string DefaultThemeId = "grev-default";

    public static IReadOnlyList<ThemeDefinition> BuiltIn { get; } =
    [
        new(DefaultThemeId, "Grev Default", "#090C12", "#11151E", "#3A465F", "#151923", "#20283A", "#7EA6FF", "#97A0B3", "#D8B65A", "#D94B55", "#747B88", IsBuiltIn: true),
        new("twilight-violet", "Twilight Violet", "#0B0A14", "#151226", "#3D3560", "#191531", "#241E42", "#B18CFF", "#9C96B8", "#D8B65A", "#D94B55", "#7C7694", IsBuiltIn: true),
        new("ember", "Ember", "#140B0A", "#241412", "#5A3630", "#241512", "#33201B", "#FF9A5A", "#B39A93", "#D8B65A", "#E8734A", "#8F7A74", IsBuiltIn: true),
        new("forest", "Forest", "#0A120E", "#111F17", "#33503E", "#132019", "#1C2E23", "#6FD6A0", "#8FA89A", "#D8B65A", "#D94B55", "#6E8478", IsBuiltIn: true),

        // Console-inspired starting points. Each is a full theme (colours + Home layout) that can
        // be selected as-is or saved as a new theme and changed in any way. Only the layout and
        // colours are evoked; no console maker's artwork, logos or sounds are included.
        new("ps5-style", "PS5 Style", "#05070F", "#0D1220", "#2A3350", "#131A2E", "#1E2A4A", "#FFFFFF", "#A3ACC2", "#D8B65A", "#D94B55", "#7C8396", IsBuiltIn: true,
            Layout: new ThemeLayout(HomeSectionMode.Merged, HomeItemFlow.Row, HomeTileShape.Square, 100, 16, 18,
                HomeContentPosition.Top, HomeFocusEffect.Grow, ShowTileLabels: false, ShowSectionHeadings: false,
                ShowWelcome: false, ShowFocusedTitle: true, FontFamily: "Segoe UI Light",
                BackgroundStyle: HomeBackgroundStyle.Gradient, BackgroundGlow: "#0B1F4D")),
        new("ps4-style", "PS4 Style", "#031B3D", "#082A57", "#2D5A93", "#0B3367", "#13468A", "#FFFFFF", "#B4C7E3", "#D8B65A", "#D94B55", "#8EA3C2", IsBuiltIn: true,
            Layout: new ThemeLayout(HomeSectionMode.Merged, HomeItemFlow.Row, HomeTileShape.Square, 90, 2, 10,
                HomeContentPosition.Top, HomeFocusEffect.Grow, ShowTileLabels: false, ShowSectionHeadings: false,
                ShowWelcome: false, ShowFocusedTitle: true,
                BackgroundStyle: HomeBackgroundStyle.Gradient, BackgroundGlow: "#1B5FB0")),
        new("xbox360-blades", "Xbox 360 Blades", "#0B1306", "#16240C", "#3F6B1E", "#1D3310", "#2C4D18", "#9BD94B", "#A8BC94", "#D8B65A", "#D94B55", "#7F8C74", IsBuiltIn: true,
            Layout: new ThemeLayout(HomeSectionMode.Blades, HomeItemFlow.Grid, HomeTileShape.Wide, 90, 0, 10,
                BackgroundStyle: HomeBackgroundStyle.Gradient, BackgroundGlow: "#3E7C14")),
        new("xbox360-tabs", "Xbox 360 Tabs", "#E9ECEF", "#FFFFFF", "#BFC6CC", "#4E9A1A", "#3D7F12", "#3D7F12", "#5A6670", "#9A7A1E", "#B8323C", "#6B737B", IsBuiltIn: true,
            Text: "#1E2328", ButtonText: "#FFFFFF",
            Layout: new ThemeLayout(HomeSectionMode.Tabs, HomeItemFlow.Grid, HomeTileShape.Wide, 100, 0, 8,
                ShowSectionHeadings: false, FontFamily: "Segoe UI Light",
                BackgroundStyle: HomeBackgroundStyle.Gradient, BackgroundGlow: "#FFFFFF")),
        new("xbox-one-style", "Xbox One Style", "#101010", "#1C1C1C", "#3A3A3A", "#1F1F1F", "#2B2B2B", "#52B043", "#A0A0A0", "#D8B65A", "#D94B55", "#7A7A7A", IsBuiltIn: true,
            Layout: new ThemeLayout(HomeSectionMode.Stacked, HomeItemFlow.Grid, HomeTileShape.Square, 90, 0, 6,
                BackgroundStyle: HomeBackgroundStyle.Solid, BackgroundGlow: "#1A1A1A")),
        new("wii-style", "Wii Style", "#E7EAED", "#FFFFFF", "#C3C9CF", "#F6F7F8", "#E3F4FB", "#1E8FC4", "#6B737B", "#9A7A1E", "#B8323C", "#6B737B", IsBuiltIn: true,
            Text: "#3A3F44", ButtonText: "#2E3439",
            Layout: new ThemeLayout(HomeSectionMode.Stacked, HomeItemFlow.Grid, HomeTileShape.Channel, 100, 22, 14,
                HomeContentPosition.Top, HomeFocusEffect.Grow, ShowSectionHeadings: false, ShowWelcome: false,
                SystemDock: true, ShowClock: true, FontFamily: "Trebuchet MS",
                BackgroundStyle: HomeBackgroundStyle.Gradient, BackgroundGlow: "#FFFFFF")),
        new("switch-style", "Switch Style", "#2D2D2D", "#3A3A3A", "#555555", "#3C3C3C", "#484848", "#00C3E3", "#B5B5B5", "#D8B65A", "#D94B55", "#8A8A8A", IsBuiltIn: true,
            Layout: new ThemeLayout(HomeSectionMode.Merged, HomeItemFlow.Row, HomeTileShape.Square, 135, 4, 16,
                HomeContentPosition.Center, HomeFocusEffect.Outline, ShowTileLabels: false, ShowSectionHeadings: false,
                ShowWelcome: false, ShowFocusedTitle: true, SystemDock: true, ShowClock: true,
                BackgroundStyle: HomeBackgroundStyle.Solid, BackgroundGlow: "#2D2D2D"))
    ];

    public static ThemeDefinition Default => BuiltIn[0];

    public static ThemeDefinition? FindBuiltIn(string id) =>
        BuiltIn.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase));
}
