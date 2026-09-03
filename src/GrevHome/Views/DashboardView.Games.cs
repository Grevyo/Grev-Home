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
        GamesSummaryText.Text = _homeGames.Count == 0
            ? "No games added yet"
            : $"Open all {_homeGames.Count} game{(_homeGames.Count == 1 ? string.Empty : "s")}";
    }

    private void Game_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameLibraryEntry game })
        {
            GameRequested?.Invoke(game);
        }
    }
}
