using System.Windows;
using System.Windows.Controls;
using GrevHome.Games;

namespace GrevHome.Views;

public partial class DashboardView
{
    private IReadOnlyList<GameLibraryEntry> _homeGames = Array.Empty<GameLibraryEntry>();

    public event Action<GameLibraryEntry>? GameRequested;

    public void SetGames(IReadOnlyList<GameLibraryEntry> games)
    {
        _homeGames = games ?? Array.Empty<GameLibraryEntry>();
        RenderGames();
    }

    private void RenderGames()
    {
        GamesPanel.Children.Clear();
        GamesSection.Visibility = _homeGames.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        GamesSummaryText.Text = _homeGames.Count == 0
            ? string.Empty
            : $"{_homeGames.Count} game{(_homeGames.Count == 1 ? string.Empty : "s")}";

        foreach (var game in _homeGames)
        {
            var available = GameLibraryService.IsSourceAvailable(game);
            var button = new Button
            {
                Style = (Style)FindResource("DashboardTileStyle"),
                Tag = game,
                IsEnabled = available,
                ToolTip = available
                    ? $"Open {game.DisplayName} through {GameLibraryService.GetPlatformDisplayName(game.Platform)}."
                    : $"Game file unavailable: {game.SourcePath}"
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = game.DisplayName,
                Style = (Style)FindResource("DashboardTileTitleStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = available
                    ? GameLibraryService.GetPlatformDisplayName(game.Platform)
                    : $"{GameLibraryService.GetPlatformDisplayName(game.Platform)} • FILE MISSING",
                Style = (Style)FindResource("DashboardTileDetailStyle")
            });
            stack.Children.Add(new TextBlock
            {
                Text = available ? "Play" : "Reconnect the game file or drive",
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource(available ? "AccentBrush" : "MutedBrush")
            });

            button.Content = stack;
            button.Click += Game_Click;
            GamesPanel.Children.Add(button);
        }
    }

    private void Game_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameLibraryEntry game })
        {
            GameRequested?.Invoke(game);
        }
    }
}
