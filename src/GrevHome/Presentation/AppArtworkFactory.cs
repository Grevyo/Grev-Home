using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GrevHome.Presentation;

/// <summary>
/// Creates the standard Grev Home app artwork surface. App tiles never use text initials
/// as artwork: package/user image first, then a neutral graphical fallback.
/// </summary>
public static class AppArtworkFactory
{
    public static FrameworkElement Create(string? assetPath, double size, double cornerRadius = 16)
    {
        var host = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 58)),
            ClipToBounds = true
        };

        var image = TryCreateImage(assetPath);
        host.Child = image ?? CreateNeutralGraphic(size);
        return host;
    }

    private static Image? TryCreateImage(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;

        try
        {
            Uri uri;
            if (Path.IsPathRooted(assetPath))
            {
                if (!File.Exists(assetPath)) return null;
                uri = new Uri(Path.GetFullPath(assetPath), UriKind.Absolute);
            }
            else
            {
                uri = new Uri(assetPath, UriKind.RelativeOrAbsolute);
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();

            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException)
        {
            return null;
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
