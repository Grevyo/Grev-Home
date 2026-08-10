using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Presentation;
using GrevHome.Sessions;
using GrevHome.Store;

namespace GrevHome.Views;

public partial class GrevStoreView : UserControl
{
    private IReadOnlyList<GrevStorePackageDefinition> _packages = Array.Empty<GrevStorePackageDefinition>();
    private string _filter = "All";
    private SessionUser? _primaryUser;

    public event Action<GrevStorePackageDefinition>? PackageRequested;

    public GrevStoreView()
    {
        InitializeComponent();
    }

    public void SetStore(IReadOnlyList<GrevStorePackageDefinition> packages, SessionUser? primaryUser)
    {
        _packages = packages;
        _primaryUser = primaryUser;
        _filter = "All";
        ContextText.Text = primaryUser?.GrevId is { Length: > 0 } grevId
            ? $"Primary User: {primaryUser.DisplayName} • {grevId}. Profile Apps install only for this GrevID."
            : "A persistent local Primary User is required for Profile App installation.";
        Render();
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void Render()
    {
        PackagesPanel.Children.Clear();
        var visible = _packages.Where(MatchesFilter).ToArray();
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var package in visible)
        {
            var button = new Button
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Height = DefaultThemeMetrics.AppTileHeight,
                Margin = new Thickness(8),
                Padding = new Thickness(0),
                Tag = package,
                IsEnabled = !package.IsProfileInstall || !string.IsNullOrWhiteSpace(_primaryUser?.GrevId),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };

            var tile = new Border
            {
                Background = CreateTileBrush(package.Presentation.TileColor),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14, 10, 14, 8)
            };

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var artwork = AppArtworkFactory.Create(
                package.Presentation.IconAsset,
                package.Presentation.TileColor,
                88,
                14);
            artwork.HorizontalAlignment = HorizontalAlignment.Center;
            artwork.VerticalAlignment = VerticalAlignment.Center;

            var name = new TextBlock
            {
                Text = package.Presentation.DisplayName,
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
            button.Content = tile;
            button.Click += Package_Click;
            PackagesPanel.Children.Add(button);
        }

        StatusText.Text = $"{visible.Length} package{(visible.Length == 1 ? string.Empty : "s")} shown • {_packages.Count} trusted installer package{(_packages.Count == 1 ? string.Empty : "s")} registered in Grev Store.";
    }

    private bool MatchesFilter(GrevStorePackageDefinition package) =>
        _filter == "All" || string.Equals(package.Category.ToString(), _filter, StringComparison.OrdinalIgnoreCase);

    private static Brush CreateTileBrush(string color)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(21, 25, 35));
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filter })
        {
            _filter = filter;
            Render();
        }
    }

    private void Package_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GrevStorePackageDefinition package })
        {
            PackageRequested?.Invoke(package);
        }
    }
}
