using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Profiles;

namespace GrevHome.Views;

/// <summary>
/// The mini profile card: avatar with presence ring, name, headline, what they are playing and a
/// short bio, on the colours they chose for their grev.dad profile card. Drawn in the profile
/// grid's card slot; friends lists use the same fields through FriendsView.CreateFriendCard.
/// </summary>
public static class ProfileMiniCardView
{
    public static FrameworkElement Create(ProfileMiniCard card, FrameworkElement resources, bool large = false)
    {
        var foreground = new SolidColorBrush(Parse(card.TextColour, Colors.White));
        var avatarSize = large ? 96 : 48;
        var ring = card.Availability switch
        {
            "online" => Color.FromRgb(0x39, 0xD9, 0x8A),
            "away" => Color.FromRgb(0xF5, 0xB8, 0x3D),
            "busy" => Color.FromRgb(0xEF, 0x5B, 0x5B),
            _ => Color.FromRgb(0x66, 0x71, 0x81)
        };
        var image = ProfileTileBoard.LoadImage(card.AvatarDataUrl ?? card.AvatarFile);
        var avatar = new Border
        {
            Width = avatarSize,
            Height = avatarSize,
            CornerRadius = new CornerRadius(avatarSize / 2.0),
            BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(ring),
            Background = new SolidColorBrush(Color.FromRgb(23, 28, 35)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = image is not null
                ? new System.Windows.Shapes.Ellipse { Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill } }
                : new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(card.DisplayName) ? "?" : card.DisplayName[..1].ToUpperInvariant(),
                    FontSize = avatarSize * 0.4,
                    FontWeight = FontWeights.Bold,
                    Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
        };

        var stack = new StackPanel { Margin = new Thickness(large ? 22 : 14) };
        stack.Children.Add(avatar);
        stack.Children.Add(new TextBlock
        {
            Text = (card.IsBestFriend ? "★ " : "") + card.DisplayName,
            Margin = new Thickness(0, large ? 14 : 8, 0, 0),
            FontSize = large ? 30 : 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var subline = string.Join("  •  ", new[]
        {
            string.IsNullOrWhiteSpace(card.Username) ? null : $"@{card.Username}",
            card.Level is { } level ? $"Level {level}" : null,
            card.IsVerified ? "✓ Verified" : null
        }.Where(part => part is not null));
        if (subline.Length > 0)
            stack.Children.Add(new TextBlock { Text = subline, Margin = new Thickness(0, 3, 0, 0), FontSize = 12, Foreground = foreground, Opacity = 0.75 });
        if (!string.IsNullOrWhiteSpace(card.Headline))
            stack.Children.Add(new TextBlock { Text = card.Headline, Margin = new Thickness(0, 10, 0, 0), FontSize = large ? 17 : 14, FontWeight = FontWeights.SemiBold, Foreground = foreground, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(card.ActivityText))
        {
            stack.Children.Add(new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0x39, 0xD9, 0x8A)),
                Child = new TextBlock { Text = $"Playing {card.ActivityText}", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(4, 20, 11)), TextTrimming = TextTrimming.CharacterEllipsis }
            });
        }
        if (!string.IsNullOrWhiteSpace(card.Bio))
            stack.Children.Add(new TextBlock { Text = card.Bio, Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = foreground, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = large ? 120 : 40 });

        var style = new Style(typeof(Button), (Style)resources.FindResource("SharpTileButtonStyle"));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new LinearGradientBrush(
            Parse(card.BackgroundPrimary, Colors.Black), Parse(card.BackgroundSecondary, Colors.Navy), card.BackgroundAngle)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Parse(card.BorderColour, Colors.Gray))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        var button = new Button { Style = style, Content = new Grid { ClipToBounds = true, Children = { stack } } };
        GrevHome.Presentation.ShellTileMotion.Attach(button);
        return button;
    }

    private static Color Parse(string value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { return fallback; }
    }
}
