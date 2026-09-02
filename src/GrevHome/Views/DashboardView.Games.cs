using System.Windows;
using System.Windows.Controls;
using GrevHome.Games;
using GrevHome.Presentation;

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

            button.Padding = new Thickness(0);
            var content = new Grid();
            var tileColor = GameArtworkFactory.GetTileColor(game);
            content.Children.Add(string.IsNullOrWhiteSpace(game.TileMediaPath)
                ? AppArtworkFactory.CreateTile(game.DisplayName, null, tileColor)
                : AppArtworkFactory.CreateFullTile(game.TileMediaPath, tileColor));
            content.Children.Add(GameArtworkFactory.CreateConsoleMark(game, available));
            button.Content = content;
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
