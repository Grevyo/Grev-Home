using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GrevHome.Online;

namespace GrevHome.Profiles;

/// <summary>Shared friend-tile and full-profile artwork treatment.</summary>
public static class PublicProfileCardStyle
{
    public static Brush Background(GrevDadPublicCard card)
    {
        var cover = ProfileAvatarShapeStyle.TryLoadDataUrl(card.CoverMedia);
        if (cover is null) return ProfileBannerCatalog.CreateBrush(card.Theme);
        var bounds = new Rect(0, 0, cover.Width, cover.Height);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(ProfileBannerCatalog.CreateBrush(card.Theme), null, new RectangleGeometry(bounds)));
        group.Children.Add(new GeometryDrawing(new ImageBrush(cover) { Stretch = Stretch.UniformToFill }, null, new RectangleGeometry(bounds)));
        var shade = new LinearGradientBrush(Color.FromArgb(70, 0, 0, 0), Color.FromArgb(215, 0, 0, 0), 90);
        group.Children.Add(new GeometryDrawing(shade, null, new RectangleGeometry(bounds)));
        var brush = new DrawingBrush(group) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        return brush;
    }

    public static Effect? FrameEffect(string frame)
    {
        if (frame != "glow") return null;
        var effect = new DropShadowEffect { Color = Color.FromRgb(112, 151, 246), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6 };
        effect.Freeze();
        return effect;
    }
}
