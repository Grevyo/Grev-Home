using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GrevHome.Games;

namespace GrevHome.Presentation;

public static class GameArtworkFactory
{
    public const string DefaultTileColor = "#0F2F6E";

    public static string GetTileColor(GameLibraryEntry game) =>
        string.IsNullOrWhiteSpace(game.TileColor) ? DefaultTileColor : game.TileColor;

    public static FrameworkElement CreateConsoleMark(GameLibraryEntry game, bool available)
    {
        FrameworkElement content;
        if (!string.IsNullOrWhiteSpace(game.IconPath))
        {
            var size = 34 * Math.Clamp(game.ConsoleLogoScale, 0.5, 2.5);
            content = AppArtworkFactory.CreateTransparent(game.IconPath, size);
        }
        else
        {
            content = new TextBlock
            {
                Text = available ? GameLibraryService.GetPlatformDisplayName(game.Platform) : "FILE MISSING",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.95 }
            };
        }

        var host = new Border
        {
            Child = content,
            Margin = new Thickness(8, 6, 8, 6),
            Padding = game.ConsoleLogoHasBackground ? new Thickness(5, 3, 5, 3) : new Thickness(0),
            Background = game.ConsoleLogoHasBackground
                ? ParseBrush(game.ConsoleLogoBackgroundColor)
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(5)
        };
        ApplyPosition(host, game.ConsoleLogoPosition);
        return host;
    }

    private static void ApplyPosition(FrameworkElement element, GameConsoleLogoPosition position)
    {
        element.HorizontalAlignment = position switch
        {
            GameConsoleLogoPosition.TopCenter or GameConsoleLogoPosition.BottomCenter => HorizontalAlignment.Center,
            GameConsoleLogoPosition.TopRight or GameConsoleLogoPosition.BottomRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };
        element.VerticalAlignment = position is GameConsoleLogoPosition.BottomLeft or GameConsoleLogoPosition.BottomCenter or GameConsoleLogoPosition.BottomRight
            ? VerticalAlignment.Bottom
            : VerticalAlignment.Top;
    }

    private static Brush ParseBrush(string? value)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value ?? "#000000"));
        }
        catch (FormatException)
        {
            return Brushes.Black;
        }
    }
}
