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
    bool IsBuiltIn = false)
{
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
    }

    private static void CheckColor(string value, string field)
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

        // Every button's own label is a fixed white, set once in the base Button style rather
        // than themed, so it is Surface/SurfaceHover - not the theme's own text color - that must
        // stay readable against it.
        Check("Button text on Surface", "#FFFFFF", Surface);
        Check("Button text on Surface Hover", "#FFFFFF", SurfaceHover);
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
        if (!HexPattern.IsMatch(hex ?? string.Empty)) return 0;
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
        new("forest", "Forest", "#0A120E", "#111F17", "#33503E", "#132019", "#1C2E23", "#6FD6A0", "#8FA89A", "#D8B65A", "#D94B55", "#6E8478", IsBuiltIn: true)
    ];

    public static ThemeDefinition Default => BuiltIn[0];

    public static ThemeDefinition? FindBuiltIn(string id) =>
        BuiltIn.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase));
}
