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
    }

    private static SolidColorBrush Brush(string hex) => new(ParseColor(hex));

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
