using System.Windows;
using System.Windows.Controls;
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
            var scope = package.IsProfileInstall ? "PROFILE APP" : "GLOBAL APP";
            var button = new Button
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Height = DefaultThemeMetrics.AppTileHeight,
                Margin = new Thickness(8),
                Tag = package,
                IsEnabled = !package.IsProfileInstall || !string.IsNullOrWhiteSpace(_primaryUser?.GrevId)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(12),
                Background = (System.Windows.Media.Brush)FindResource("SurfaceHoverBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = package.Presentation.FallbackGlyph,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = package.Presentation.DisplayName,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{package.Category} • {scope}",
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
            });
            text.Children.Add(new TextBlock
            {
                Text = package.App.Description ?? string.Empty,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 42
            });

            Grid.SetColumn(text, 1);
            grid.Children.Add(glyph);
            grid.Children.Add(text);
            button.Content = grid;
            button.Click += Package_Click;
            PackagesPanel.Children.Add(button);
        }

        StatusText.Text = $"{visible.Length} package{(visible.Length == 1 ? string.Empty : "s")} shown • {_packages.Count} trusted installer package{(_packages.Count == 1 ? string.Empty : "s")} registered in Grev Store.";
    }

    private bool MatchesFilter(GrevStorePackageDefinition package) =>
        _filter == "All" || string.Equals(package.Category.ToString(), _filter, StringComparison.OrdinalIgnoreCase);

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
