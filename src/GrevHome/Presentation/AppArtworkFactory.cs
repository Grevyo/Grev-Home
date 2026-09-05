using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GrevHome.Presentation;

/// <summary>
/// Creates Grev Home app presentation surfaces. App tiles never use text initials as artwork:
/// package/user image first, then a neutral graphical fallback. The complete standard app tile
/// is also rendered here so Store, Installed Apps and app detail pages share one layout.
/// </summary>
public static class AppArtworkFactory
{
    private const string SteamBuiltInAsset = "builtin://steam";
    private static readonly Color DefaultBackground = Color.FromRgb(31, 40, 58);

    public static FrameworkElement Create(string? assetPath, double size, double cornerRadius = 16) =>
        Create(assetPath, null, size, size, cornerRadius);

    public static FrameworkElement Create(
        string? assetPath,
        string? backgroundColor,
        double size,
        double cornerRadius = 16) =>
        Create(assetPath, backgroundColor, size, size, cornerRadius);

    public static FrameworkElement Create(
        string? assetPath,
        string? backgroundColor,
        double width,
        double height,
        double cornerRadius = 16)
    {
        var referenceSize = Math.Min(width, height);
        var host = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new SolidColorBrush(ParseColor(backgroundColor)),
            ClipToBounds = true,
            Padding = new Thickness(Math.Max(4, referenceSize * 0.08))
        };

        var builtIn = TryCreateBuiltInGraphic(assetPath, referenceSize);
        var image = builtIn is null ? TryCreateImage(assetPath) : null;
        host.Child = builtIn ?? image ?? CreateNeutralGraphic(referenceSize);
        return host;
    }

    public static FrameworkElement CreateTile(
        string displayName,
        string? assetPath,
        string? backgroundColor)
    {
        var tile = new Border
        {
            Background = new SolidColorBrush(ParseColor(backgroundColor)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 10, 14, 8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var artwork = Create(assetPath, backgroundColor, 88, 14);
        artwork.HorizontalAlignment = HorizontalAlignment.Center;
        artwork.VerticalAlignment = VerticalAlignment.Center;

        var name = new TextBlock
        {
            Text = displayName,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(name, 1);

        content.Children.Add(artwork);
        content.Children.Add(name);
        tile.Child = content;
        return tile;
    }

    public static FrameworkElement CreateFullTile(
        string assetPath,
        string? backgroundColor,
        double width = DefaultThemeMetrics.AppTileWidth,
        double height = DefaultThemeMetrics.AppTileHeight)
    {
        var host = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(ParseColor(backgroundColor)),
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true
        };
        var image = TryCreateImage(assetPath);
        if (image is not null)
        {
            image.Stretch = Stretch.UniformToFill;
            host.Child = image;
        }
        else
        {
            host.Child = Create(assetPath, backgroundColor, width, height, 9);
        }
        return host;
    }

    public static FrameworkElement CreateTransparent(string? assetPath, double size)
    {
        var image = TryCreateImage(assetPath);
        if (image is null) return new Grid { Width = size, Height = size, Background = Brushes.Transparent };
        image.Width = size;
        image.Height = size;
        image.Stretch = Stretch.Uniform;
        return image;
    }

    private static FrameworkElement? TryCreateBuiltInGraphic(string? assetPath, double size)
    {
        if (assetPath?.StartsWith("builtin://dashboard/", StringComparison.OrdinalIgnoreCase) == true)
        {
            var glyph = assetPath[20..].ToLowerInvariant() switch
            {
                "games" => "\uE7FC",
                "apps" => "\uE71D",
                "store" => "\uE719",
                "files" => "\uE8B7",
                "running" => "\uE768",
                "activity" => "\uE7ED",
                "killer" => "\uE711",
                "settings" => "\uE713",
                "account" => "\uE77B",
                "controller" => "\uE7FC",
                "audio" => "\uE767",
                "display" => "\uE7F4",
                "connections" => "\uE701",
                "system" => "\uE946",
                "theme" => "\uE771",
                "power" => "\uE7E8",
                "admin" => "\uE77B",
                "friends" => "\uE716",
                "web" => "\uE774",
                _ => "\uE10C"
            };
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size * 0.58,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        if (!string.Equals(assetPath, SteamBuiltInAsset, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            // Steam's package tile needs a default before Steam exists on disk. This vector is kept
            // inside the shared artwork factory so presentation still flows through the same package
            // defaults / GrevID override / Reset to App Default contract as every other app.
            var geometry = Geometry.Parse(
                "M11.979,0 C5.678,0 .511,4.86 .022,11.037 L6.454,13.695 C6.999,13.324 7.657,13.105 8.366,13.105 C8.429,13.105 8.491,13.109 8.554,13.111 L11.415,8.969 L11.415,8.91 C11.415,6.415 13.443,4.386 15.939,4.386 C18.433,4.386 20.463,6.417 20.463,8.913 C20.463,11.409 18.433,13.438 15.939,13.438 L15.834,13.438 L11.758,16.349 C11.758,16.401 11.762,16.454 11.762,16.508 C11.762,18.383 10.247,19.904 8.372,19.904 C6.737,19.904 5.356,18.731 5.041,17.177 L.436,15.27 C1.862,20.307 6.486,24 11.979,24 C18.606,24 23.978,18.627 23.978,12 C23.978,5.373 18.605,0 11.979,0 Z M7.54,18.21 L6.067,17.6 C6.329,18.143 6.781,18.599 7.381,18.85 C8.678,19.389 10.174,18.774 10.713,17.475 C10.976,16.845 10.977,16.156 10.718,15.526 C10.459,14.896 9.968,14.405 9.341,14.143 C8.717,13.883 8.051,13.894 7.463,14.113 L8.986,14.743 C9.942,15.143 10.395,16.243 9.995,17.198 C9.598,18.155 8.498,18.608 7.541,18.21 Z M18.955,8.907 C18.955,7.245 17.602,5.892 15.94,5.892 C14.275,5.892 12.925,7.245 12.925,8.907 C12.925,10.572 14.275,11.922 15.94,11.922 C17.603,11.922 18.955,10.572 18.955,8.907 Z M13.682,8.902 C13.682,7.65 14.695,6.636 15.947,6.636 C17.196,6.636 18.213,7.65 18.213,8.902 C18.213,10.153 17.196,11.167 15.947,11.167 C14.694,11.167 13.682,10.153 13.682,8.902 Z");

            return new Viewbox
            {
                Width = size * 0.72,
                Height = size * 0.72,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Child = new System.Windows.Shapes.Path
                {
                    Data = geometry,
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform
                }
            };
        }
        catch (FormatException)
        {
            // A built-in branding parse failure must degrade to the neutral package artwork,
            // never take down Store/Installed Apps while rendering a tile.
            return null;
        }
    }

    private static Image? TryCreateImage(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;

        try
        {
            BitmapImage bitmap;
            if (assetPath.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                bitmap = CreateFromDataUri(assetPath);
            }
            else
            {
                Uri uri;
                if (System.IO.Path.IsPathRooted(assetPath))
                {
                    if (!File.Exists(assetPath)) return null;
                    uri = new Uri(System.IO.Path.GetFullPath(assetPath), UriKind.Absolute);
                }
                else
                {
                    uri = new Uri(assetPath, UriKind.RelativeOrAbsolute);
                }

                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = uri;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    private static BitmapImage CreateFromDataUri(string dataUri)
    {
        var comma = dataUri.IndexOf(',');
        if (comma <= 0 || !dataUri[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Unsupported embedded app artwork URI.");
        }

        var bytes = Convert.FromBase64String(dataUri[(comma + 1)..]);
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultBackground;

        try
        {
            return ColorConverter.ConvertFromString(value) is Color color
                ? color
                : DefaultBackground;
        }
        catch (FormatException)
        {
            return DefaultBackground;
        }
        catch (NotSupportedException)
        {
            return DefaultBackground;
        }
    }

    private static FrameworkElement CreateNeutralGraphic(double size)
    {
        var canvas = new Grid();

        var outer = new Ellipse
        {
            Width = size * 0.54,
            Height = size * 0.54,
            Stroke = new SolidColorBrush(Color.FromRgb(151, 160, 179)),
            StrokeThickness = Math.Max(2, size * 0.035),
            Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var inner = new Rectangle
        {
            Width = size * 0.24,
            Height = size * 0.24,
            RadiusX = size * 0.04,
            RadiusY = size * 0.04,
            Fill = new SolidColorBrush(Color.FromRgb(126, 166, 255)),
            Opacity = 0.9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(45)
        };

        canvas.Children.Add(outer);
        canvas.Children.Add(inner);
        return canvas;
    }
}
