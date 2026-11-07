using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrevHome.Presentation;
using GrevHome.Profiles;

namespace GrevHome.Views;

/// <summary>
/// Draws a profile's tile grid the way grev.dad lays it out: eight columns, tiles at their saved
/// cells, the mini profile card in its 4 × 6 slot, and live widgets filled in. Every tile is a
/// focusable button, so a controller moves through the profile tile by tile and the page scrolls
/// with it; tiles with their own targets (a friend in Best friends, a link tile) open them on A.
/// </summary>
public sealed class ProfileTileBoard : Border
{
    private const double Gap = 10;
    private readonly Canvas _canvas = new();
    private ProfileSpace? _space;
    private double _lastWidth;

    /// <summary>A friend in a Best friends widget was chosen (their grev.dad user ID).</summary>
    public event Action<string>? FriendRequested;
    /// <summary>A link tile was chosen.</summary>
    public event Action<string>? LinkRequested;

    public ProfileTileBoard()
    {
        Child = _canvas;
        SizeChanged += (_, args) =>
        {
            if (Math.Abs(args.NewSize.Width - _lastWidth) > 1) Render();
        };
    }

    public void SetSpace(ProfileSpace? space)
    {
        _space = space;
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        _lastWidth = ActualWidth;
        var space = _space;
        if (space is null || ActualWidth <= 0)
        {
            _canvas.Height = 0;
            return;
        }

        var cell = (ActualWidth - Gap * (ProfileTileGrid.Columns - 1)) / ProfileTileGrid.Columns;
        var row = Math.Clamp(cell * 0.72, 64, 120);
        var bottom = space.Tiles.Select(tile => tile.Y + tile.Height).DefaultIfEmpty(0).Max();
        if (space.ShowsCardSlot) bottom = Math.Max(bottom, space.CardY + ProfileSpace.CardRows);
        _canvas.Width = ActualWidth;
        _canvas.Height = Math.Max(0, bottom * (row + Gap) - Gap);

        if (space.ShowsCardSlot && space.Card is { } card)
        {
            var element = ProfileMiniCardView.Create(card, this, large: true);
            Place(element, space.CardX, space.CardY, ProfileSpace.CardColumns, ProfileSpace.CardRows, cell, row);
        }

        foreach (var tile in space.Tiles.OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
        {
            Place(CreateTile(tile, space), tile.X, tile.Y, tile.Width, tile.Height, cell, row);
        }
    }

    private void Place(FrameworkElement element, int x, int y, int width, int height, double cell, double row)
    {
        element.Width = width * cell + (width - 1) * Gap;
        element.Height = height * row + (height - 1) * Gap;
        Canvas.SetLeft(element, x * (cell + Gap));
        Canvas.SetTop(element, y * (row + Gap));
        _canvas.Children.Add(element);
    }

    private FrameworkElement CreateTile(ProfileTile tile, ProfileSpace space)
    {
        var foreground = Brush(tile.TextColour, Colors.White);
        var content = new Grid { ClipToBounds = true };
        if (tile.BackgroundType == ProfileTileBackgroundType.Media && tile.MediaOverlay != ProfileTileMediaOverlay.None)
        {
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(tile.MediaOverlay == ProfileTileMediaOverlay.Dark ? Colors.Black : Colors.White)
                {
                    Opacity = tile.MediaOverlay == ProfileTileMediaOverlay.Dark ? 0.42 : 0.26
                }
            });
        }

        var stack = new StackPanel { Margin = new Thickness(14) };
        var widget = tile.Widget is { } kind ? ProfileWidgets.Info(kind) : null;
        stack.Children.Add(Text(widget?.Label.ToUpperInvariant() ?? KindLabel(tile), 10, foreground, FontWeights.Bold, 0.7));
        stack.Children.Add(new TextBlock
        {
            Text = tile.Title ?? widget?.Label ?? KindLabel(tile),
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = tile.Height == 1 ? 15 : 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            FontFamily = TileFont(tile.FontFamily),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        Action? activate = null;
        if (widget is not null)
        {
            var view = space.Widgets.TryGetValue(tile.TileId, out var found) ? found : ProfileWidgetView.Message(widget.Kind, "Loading…");
            AddWidget(stack, view, foreground);
        }
        else
        {
            if (tile.Kind == ProfileTileKind.Stat && !string.IsNullOrWhiteSpace(tile.StatValue))
                stack.Children.Add(Text(tile.StatValue, 30, foreground, FontWeights.Bold));
            if (!string.IsNullOrWhiteSpace(tile.Body))
                stack.Children.Add(new TextBlock { Text = tile.Body, Margin = new Thickness(0, 6, 0, 0), FontSize = 13, Foreground = foreground, TextWrapping = TextWrapping.Wrap, FontFamily = TileFont(tile.FontFamily) });
            if (tile.Kind == ProfileTileKind.Link && !string.IsNullOrWhiteSpace(tile.LinkUrl))
            {
                stack.Children.Add(Text($"↗ {tile.LinkLabel ?? "Open link"}", 12, foreground, FontWeights.Bold));
                var url = tile.LinkUrl;
                activate = () => LinkRequested?.Invoke(url);
            }
        }
        content.Children.Add(stack);

        // Friends in a Best friends widget are their own buttons, so the tile itself is a plain
        // container there; every other tile is one focusable button.
        if (tile.Widget == ProfileWidgetKind.BestFriends)
        {
            return new Border
            {
                Background = TileBackground(tile, space),
                BorderBrush = Brush(tile.BorderColour, Colors.Gray),
                BorderThickness = new Thickness(1),
                Child = content
            };
        }

        var style = new Style(typeof(Button), (Style)FindResource("SharpTileButtonStyle"));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TileBackground(tile, space)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(tile.BorderColour, Colors.Gray)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        var button = new Button { Style = style, Content = content, ToolTip = tile.Title };
        ShellTileMotion.Attach(button);
        if (activate is not null) button.Click += (_, _) => activate();
        return button;
    }

    private void AddWidget(StackPanel stack, ProfileWidgetView view, Brush foreground)
    {
        if (!string.IsNullOrWhiteSpace(view.Subtitle))
            stack.Children.Add(new TextBlock { Text = view.Subtitle, Margin = new Thickness(0, 4, 0, 0), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = foreground, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(view.Highlight))
        {
            stack.Children.Add(new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0x39, 0xD9, 0x8A)),
                Child = new TextBlock { Text = view.Highlight, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(4, 20, 11)), TextTrimming = TextTrimming.CharacterEllipsis }
            });
        }
        if (!string.IsNullOrWhiteSpace(view.Body))
            stack.Children.Add(new TextBlock { Text = view.Body, Margin = new Thickness(0, 6, 0, 0), FontSize = 13, Foreground = foreground, TextWrapping = TextWrapping.Wrap });

        if (view.Stats.Count > 0)
        {
            var grid = new UniformGrid { Columns = Math.Min(3, view.Stats.Count), Margin = new Thickness(0, 8, 0, 0) };
            foreach (var stat in view.Stats)
            {
                grid.Children.Add(new StackPanel
                {
                    Margin = new Thickness(0, 0, 8, 8),
                    Children =
                    {
                        Text(stat.Value, 22, foreground, FontWeights.Bold),
                        Text(stat.Label.ToUpperInvariant(), 10, foreground, FontWeights.Bold, 0.7)
                    }
                });
            }
            stack.Children.Add(grid);
        }

        if (view.Lines.Count > 0)
        {
            if (view.Kind == ProfileWidgetKind.BestFriends)
            {
                var friends = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var line in view.Lines) friends.Children.Add(CreateFriendChip(line));
                stack.Children.Add(friends);
            }
            else
            {
                foreach (var line in view.Lines) stack.Children.Add(CreateLine(line, foreground));
            }
        }

        if (!string.IsNullOrWhiteSpace(view.EmptyText))
            stack.Children.Add(new TextBlock { Text = view.EmptyText, Margin = new Thickness(0, 6, 0, 0), FontSize = 12, FontStyle = FontStyles.Italic, Foreground = foreground, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
    }

    private static FrameworkElement CreateLine(ProfileWidgetLine line, Brush foreground)
    {
        var dock = new DockPanel { LastChildFill = true };
        if (LoadImage(line.ImageUrl) is { } image)
        {
            var picture = new Image { Source = image, Width = 32, Height = 32, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(picture, Dock.Left);
            dock.Children.Add(picture);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = line.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = foreground, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(line.Detail))
            text.Children.Add(new TextBlock { Text = line.Detail, FontSize = 11, Foreground = foreground, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });
        dock.Children.Add(text);
        return new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
            Child = dock
        };
    }

    private FrameworkElement CreateFriendChip(ProfileWidgetLine friend)
    {
        var ring = friend.Availability switch
        {
            "online" => Color.FromRgb(0x39, 0xD9, 0x8A),
            "away" => Color.FromRgb(0xF5, 0xB8, 0x3D),
            "busy" => Color.FromRgb(0xEF, 0x5B, 0x5B),
            _ => Color.FromRgb(0x66, 0x71, 0x81)
        };
        var avatar = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(26),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(ring),
            Background = new SolidColorBrush(Color.FromRgb(23, 28, 35)),
            ClipToBounds = true,
            Child = LoadImage(friend.ImageUrl) is { } image
                ? new System.Windows.Shapes.Ellipse { Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill } }
                : new TextBlock { Text = friend.Title[..1].ToUpperInvariant(), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var panel = new StackPanel { Width = 86 };
        panel.Children.Add(avatar);
        panel.Children.Add(new TextBlock { Text = friend.Title, Margin = new Thickness(0, 4, 0, 0), FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(friend.Detail))
            panel.Children.Add(new TextBlock { Text = friend.Detail, FontSize = 10, Opacity = 0.75, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var style = new Style(typeof(Button), (Style)FindResource("SharpTileButtonStyle"));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        var button = new Button { Style = style, Content = panel, Margin = new Thickness(0, 0, 8, 8), ToolTip = friend.Title };
        ShellTileMotion.Attach(button);
        if (friend.UserId is { } userId) button.Click += (_, _) => FriendRequested?.Invoke(userId);
        return button;
    }

    private static TextBlock Text(string text, double size, Brush foreground, FontWeight weight, double opacity = 1) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = foreground,
        Opacity = opacity,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static string KindLabel(ProfileTile tile) => tile.Kind switch
    {
        ProfileTileKind.Media => "PICTURE",
        ProfileTileKind.Link => "LINK",
        ProfileTileKind.Stat => "STAT",
        _ => "TEXT"
    };

    private static Brush TileBackground(ProfileTile tile, ProfileSpace space)
    {
        if (tile.BackgroundType == ProfileTileBackgroundType.Gradient)
            return new LinearGradientBrush(Colour(tile.BackgroundPrimary, Colors.Black), Colour(tile.BackgroundSecondary, Colors.Black), tile.BackgroundAngle);
        if (tile.BackgroundType == ProfileTileBackgroundType.Media)
        {
            ImageSource? image = null;
            if (space.MediaDataUrls.TryGetValue(tile.TileId, out var dataUrl)) image = ProfileAvatarShapeStyle.TryLoadDataUrl(dataUrl);
            else if (!string.IsNullOrWhiteSpace(tile.BackgroundMediaFile) && space.LocalMediaRoot is { } root)
                image = LoadImage(Path.Combine(root, Path.GetFileName(tile.BackgroundMediaFile)));
            if (image is not null)
            {
                return new ImageBrush(image)
                {
                    Stretch = tile.MediaFit switch
                    {
                        ProfileTileMediaFit.Contain => Stretch.Uniform,
                        ProfileTileMediaFit.Stretch => Stretch.Fill,
                        _ => Stretch.UniformToFill
                    }
                };
            }
        }
        return new SolidColorBrush(Colour(tile.BackgroundPrimary, Colors.Black));
    }

    /// <summary>A data URL, an https URL (RetroAchievements badges) or a local file.</summary>
    internal static ImageSource? LoadImage(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return ProfileAvatarShapeStyle.TryLoadDataUrl(source);
        try
        {
            var uri = source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(source, UriKind.Absolute)
                : File.Exists(source) ? new Uri(Path.GetFullPath(source), UriKind.Absolute) : null;
            if (uri is null) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.DecodePixelWidth = 128;
            bitmap.CacheOption = uri.IsFile ? BitmapCacheOption.OnLoad : BitmapCacheOption.Default;
            bitmap.EndInit();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    private static Brush Brush(string value, Color fallback) => new SolidColorBrush(Colour(value, fallback));

    private static Color Colour(string value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { return fallback; }
    }

    private static FontFamily TileFont(ProfileTileFontFamily family) => new(family switch
    {
        ProfileTileFontFamily.Display => "Impact",
        ProfileTileFontFamily.Mono => "Consolas",
        ProfileTileFontFamily.Serif => "Georgia",
        ProfileTileFontFamily.Rounded => "Trebuchet MS",
        _ => "Segoe UI"
    });
}
