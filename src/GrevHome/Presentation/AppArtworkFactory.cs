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

        var image = TryCreateImage(assetPath);
        host.Child = image ?? CreateNeutralGraphic(referenceSize);
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
