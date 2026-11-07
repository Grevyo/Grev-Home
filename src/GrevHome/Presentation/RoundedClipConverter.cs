using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GrevHome.Presentation;

/// <summary>
/// Builds a rounded-rectangle clip from (ActualWidth, ActualHeight, CornerRadius) so tile artwork
/// is cut to the theme's tile shape. A Border's CornerRadius alone only rounds its own background;
/// it does not clip children, so square artwork would otherwise poke out of round tiles.
/// </summary>
public sealed class RoundedClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not double width || values[1] is not double height ||
            width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var radius = values[2] is CornerRadius corners ? corners.TopLeft : 0;
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        clip.Freeze();
        return clip;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
