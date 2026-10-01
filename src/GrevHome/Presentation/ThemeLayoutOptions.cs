namespace GrevHome.Presentation;

/// <summary>
/// One editable Home layout setting in the Theme Creator. Every setting is a cycle or a step so it
/// works with a controller: A on the setting moves to the next value; numeric settings also have
/// − and + buttons.
/// </summary>
public sealed record ThemeLayoutOption(
    string Key,
    Func<ThemeLayout, string> Describe,
    Func<ThemeLayout, ThemeLayout> Next,
    Func<ThemeLayout, ThemeLayout>? Previous = null);

public static class ThemeLayoutOptions
{
    public static IReadOnlyList<ThemeLayoutOption> All { get; } =
    [
        new("sections", layout => $"Sections: {Describe(layout.SectionMode)}",
            layout => layout with { SectionMode = Cycle(layout.SectionMode) }),
        new("flow", layout => $"Tiles: {(layout.ItemFlow == HomeItemFlow.Grid ? "Grid" : "Row")}",
            layout => layout with { ItemFlow = Cycle(layout.ItemFlow) }),
        new("shape", layout => $"Shape: {layout.TileShape}",
            layout => layout with { TileShape = Cycle(layout.TileShape) }),
        new("size", layout => $"Tile size: {layout.TileScalePercent}%",
            layout => layout with { TileScalePercent = Math.Min(ThemeLayout.MaximumTileScale, layout.TileScalePercent + 10) },
            layout => layout with { TileScalePercent = Math.Max(ThemeLayout.MinimumTileScale, layout.TileScalePercent - 10) }),
        new("corners", layout => layout.TileShape == HomeTileShape.Circle ? "Corners: round" : $"Corners: {layout.CornerRadius}",
            layout => layout with { CornerRadius = Math.Min(ThemeLayout.MaximumCornerRadius, layout.CornerRadius + 4) },
            layout => layout with { CornerRadius = Math.Max(0, layout.CornerRadius - 4) }),
        new("spacing", layout => $"Spacing: {layout.TileSpacing}",
            layout => layout with { TileSpacing = Math.Min(ThemeLayout.MaximumTileSpacing, layout.TileSpacing + 2) },
            layout => layout with { TileSpacing = Math.Max(0, layout.TileSpacing - 2) }),
        new("position", layout => $"Position: {(layout.ContentPosition == HomeContentPosition.Center ? "Centre" : layout.ContentPosition.ToString())}",
            layout => layout with { ContentPosition = Cycle(layout.ContentPosition) }),
        new("focus", layout => $"Focus: {Describe(layout.FocusEffect)}",
            layout => layout with { FocusEffect = Cycle(layout.FocusEffect) }),
        new("labels", layout => $"Tile labels: {OnOff(layout.ShowTileLabels)}",
            layout => layout with { ShowTileLabels = !layout.ShowTileLabels }),
        new("headings", layout => $"Section headings: {OnOff(layout.ShowSectionHeadings)}",
            layout => layout with { ShowSectionHeadings = !layout.ShowSectionHeadings }),
        new("welcome", layout => $"Welcome: {OnOff(layout.ShowWelcome)}",
            layout => layout with { ShowWelcome = !layout.ShowWelcome }),
        new("focusedTitle", layout => layout.ShowTileLabels
                ? $"Focused title: {OnOff(layout.ShowFocusedTitle)}"
                : "Focused title: On (labels off)",
            layout => layout with { ShowFocusedTitle = !layout.ShowFocusedTitle }),
        new("dock", layout => $"System dock: {OnOff(layout.SystemDock)}",
            layout => layout with { SystemDock = !layout.SystemDock }),
        new("clock", layout => $"Clock: {OnOff(layout.ShowClock)}",
            layout => layout with { ShowClock = !layout.ShowClock }),
        new("font", layout => $"Font: {layout.FontFamily}",
            layout => layout with { FontFamily = NextFont(layout.FontFamily) }),
        new("background", layout => $"Background: {layout.BackgroundStyle}",
            layout => layout with { BackgroundStyle = Cycle(layout.BackgroundStyle) })
    ];

    public static string Describe(HomeSectionMode mode) => mode switch
    {
        HomeSectionMode.Merged => "One row",
        HomeSectionMode.Tabs => "Tabs",
        HomeSectionMode.Blades => "Blades",
        _ => "Stacked rows"
    };

    public static string Describe(HomeFocusEffect effect) => effect switch
    {
        HomeFocusEffect.Grow => "Grow",
        HomeFocusEffect.Outline => "Outline",
        _ => "Lift & glow"
    };

    private static string OnOff(bool value) => value ? "On" : "Off";

    private static T Cycle<T>(T value) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[(Array.IndexOf(values, value) + 1) % values.Length];
    }

    private static string NextFont(string current)
    {
        var fonts = ThemeLayout.Fonts;
        var index = fonts.ToList().FindIndex(font => string.Equals(font, current, StringComparison.OrdinalIgnoreCase));
        return fonts[(index + 1) % fonts.Count];
    }
}
