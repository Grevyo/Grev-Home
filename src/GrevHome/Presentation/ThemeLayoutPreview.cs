using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GrevHome.Presentation;

/// <summary>
/// A miniature, schematic Home for the Theme Creator: the theme's background, colours and layout
/// (sections, tabs or blades, tile shape, size, spacing, position, dock) drawn at one fifth of a
/// 1080p screen. It shows the arrangement while editing, since Home itself is not on screen.
/// </summary>
public static class ThemeLayoutPreview
{
    private const double Scale = 0.2;

    public static FrameworkElement Create(ThemeDefinition theme, double width, double height)
    {
        var layout = theme.EffectiveLayout;
        var accent = Brush(theme.Accent);
        var text = Brush(theme.EffectiveText);
        var muted = Brush(theme.Muted);
        var surface = Brush(theme.Surface);
        var card = Brush(theme.CardBackground);

        var root = new Grid
        {
            Width = width,
            Height = height,
            Background = ThemeApplier.CreateShellBackground(theme.WindowBackground, layout),
            ClipToBounds = true
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        if (layout.SectionMode == HomeSectionMode.Blades)
        {
            var blades = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 8, 0, 8) };
            for (var index = 0; index < 4; index++)
            {
                blades.Children.Add(new Border
                {
                    Width = index == 0 ? 22 : 11,
                    Margin = new Thickness(0, 0, 2, 0),
                    Background = accent,
                    Opacity = index == 0 ? 1 : 0.75 - index * 0.12
                });
            }
            root.Children.Add(blades);
        }

        var content = new Grid { Margin = new Thickness(64 * Scale, 38 * Scale, 64 * Scale, 10) };
        Grid.SetColumn(content, 1);
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        var header = new StackPanel();
        if (layout.ShowWelcome) header.Children.Add(Bar(70, 7, text, new Thickness(0, 0, 0, 6)));
        if (layout.FocusedTitleVisible) header.Children.Add(Bar(95, 8, text, new Thickness(0, 0, 0, 6)));
        if (layout.SectionMode == HomeSectionMode.Tabs)
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            for (var index = 0; index < 4; index++)
            {
                tabs.Children.Add(new Border
                {
                    Width = 26,
                    Height = 6,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = index == 0 ? text : muted,
                    BorderBrush = accent,
                    BorderThickness = new Thickness(0, 0, 0, index == 0 ? 2 : 0)
                });
            }
            header.Children.Add(tabs);
        }
        content.Children.Add(header);

        var sections = new StackPanel
        {
            Orientation = layout.SectionMode == HomeSectionMode.Merged ? Orientation.Horizontal : Orientation.Vertical,
            VerticalAlignment = layout.ItemFlow == HomeItemFlow.Row && layout.SectionMode != HomeSectionMode.Stacked
                ? layout.ContentPosition switch
                {
                    HomeContentPosition.Center => VerticalAlignment.Center,
                    HomeContentPosition.Bottom => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Top
                }
                : VerticalAlignment.Top
        };
        Grid.SetRow(sections, 1);
        var sectionCount = layout.SectionMode switch
        {
            HomeSectionMode.Stacked => 3,
            HomeSectionMode.Merged => 2,
            _ => 1
        };
        var tilesPerSection = layout.ItemFlow == HomeItemFlow.Grid && layout.SectionMode != HomeSectionMode.Merged ? 9 : 6;
        var firstTile = true;
        for (var section = 0; section < sectionCount; section++)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, layout.SectionMode == HomeSectionMode.Merged ? 0 : 5) };
            if (layout.ShowSectionHeadings && layout.SectionMode == HomeSectionMode.Stacked)
                block.Children.Add(Bar(30, 3, muted, new Thickness(2, 0, 0, 3)));
            var tiles = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                MaxWidth = layout.ItemFlow == HomeItemFlow.Grid && layout.SectionMode != HomeSectionMode.Merged
                    ? width - 64 * Scale * 2 - (layout.SectionMode == HomeSectionMode.Blades ? 60 : 0)
                    : double.PositiveInfinity
            };
            for (var index = 0; index < tilesPerSection; index++)
            {
                tiles.Children.Add(Tile(layout, firstTile ? accent : null, (section + index) % 3 == 0 ? accent : surface));
                firstTile = false;
            }
            block.Children.Add(tiles);
            sections.Children.Add(block);
        }
        content.Children.Add(sections);
        root.Children.Add(content);

        if (layout.SystemDock || layout.ShowClock)
        {
            var dock = new Grid { Height = 20, Background = card };
            Grid.SetRow(dock, 1);
            Grid.SetColumnSpan(dock, 2);
            if (layout.SystemDock)
            {
                var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                for (var index = 0; index < 5; index++)
                    icons.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(6), Margin = new Thickness(3, 0, 3, 0), Background = surface });
                dock.Children.Add(icons);
            }
            if (layout.ShowClock)
                dock.Children.Add(Bar(22, 7, text, new Thickness(0, 0, 10, 0), HorizontalAlignment.Right));
            root.Children.Add(dock);
        }

        return root;
    }

    private static FrameworkElement Tile(ThemeLayout layout, Brush? focusBorder, Brush fill)
    {
        var (width, height) = layout.TileSize;
        var focused = focusBorder is not null;
        var grow = focused ? layout.FocusScale : 1;
        return new Border
        {
            Width = width * Scale * grow,
            Height = height * Scale * grow,
            Margin = new Thickness(Math.Max(0.5, layout.TileSpacing * Scale)),
            CornerRadius = new CornerRadius(layout.EffectiveCornerRadius * Scale * grow),
            Background = fill,
            BorderBrush = focusBorder,
            BorderThickness = new Thickness(focused ? 1.5 : 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Border Bar(double width, double height, Brush fill, Thickness margin, HorizontalAlignment alignment = HorizontalAlignment.Left) =>
        new()
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = fill,
            Margin = margin,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
