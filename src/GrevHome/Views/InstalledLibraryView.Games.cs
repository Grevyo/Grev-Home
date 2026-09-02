using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Games;
using GrevHome.Presentation;
using GrevHome.Sessions;

namespace GrevHome.Views;

public partial class InstalledLibraryView
{
    private IReadOnlyList<GameLibraryEntry> _games = Array.Empty<GameLibraryEntry>();
    private bool _gameUiInitialized;

    public event EventHandler? AddGameRequested;
    public event Action<GameLibraryEntry>? GameLaunchRequested;
    public event Action<GameLibraryEntry>? GameSettingsRequested;

    public void InitializeGameLibraryUi()
    {
        if (_gameUiInitialized)
        {
            return;
        }

        _gameUiInitialized = true;

        // InstalledLibraryView owns the existing app filter. Render the game layer after that
        // filter has updated so Games/All can independently show or hide the two tile surfaces
        // without changing the established installed-app rendering contract in this first pass.
        RoutedEventHandler refresh = (_, _) => Dispatcher.BeginInvoke(new Action(RenderGames));
        AllFilterButton.Click += refresh;
        AppsFilterButton.Click += refresh;
        EmulatorsFilterButton.Click += refresh;
        ToolsFilterButton.Click += refresh;
        GamesFilterButton.Click += refresh;
    }

    public void SetGames(IReadOnlyList<GameLibraryEntry> games, SessionUser? primaryUser)
    {
        _games = games ?? Array.Empty<GameLibraryEntry>();
        AddGameButton.IsEnabled = primaryUser?.GrevId is not null;
        AddGameButton.ToolTip = primaryUser?.GrevId is null
            ? "A persistent Primary GrevID is required to add games."
            : "Add an individual game to this GrevID's library.";
        RenderGames();
    }

    public void ShowGameStatus(string message) => StatusText.Text = message;

    private void RenderGames()
    {
        GamesPanel.Children.Clear();
        var gamesOnly = string.Equals(_filter, "Games", StringComparison.OrdinalIgnoreCase);
        var showGames = gamesOnly || string.Equals(_filter, "All", StringComparison.OrdinalIgnoreCase);

        GamesSection.Visibility = showGames ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = gamesOnly ? Visibility.Collapsed : Visibility.Visible;
        if (gamesOnly)
        {
            EmptyText.Visibility = Visibility.Collapsed;
        }

        if (!showGames)
        {
            return;
        }

        NoGamesText.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GamesCountText.Text = _games.Count == 0
            ? ""
            : $"{_games.Count} game{(_games.Count == 1 ? string.Empty : "s")}";

        var pcsx2Package = _storeCatalog.Find("pcsx2");
        foreach (var game in _games)
        {
            var available = GameLibraryService.IsSourceAvailable(game);
            var tileColor = pcsx2Package?.Presentation.TileColor ?? "#0F2F6E";
            var tile = string.IsNullOrWhiteSpace(game.TileMediaPath)
                ? AppArtworkFactory.CreateTile(
                    game.DisplayName,
                    game.IconPath ?? pcsx2Package?.Presentation.IconAsset,
                    tileColor)
                : AppArtworkFactory.CreateFullTile(game.TileMediaPath, tileColor);

            var content = new Grid();
            content.Children.Add(tile);
            var platformBadge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 6, 8, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = new TextBlock
                {
                    Text = available ? "PLAYSTATION 2" : "FILE MISSING",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.95 },
                    Foreground = available
                        ? Brushes.White
                        : new SolidColorBrush(Color.FromRgb(255, 118, 118))
                }
            };
            content.Children.Add(platformBadge);

            var button = new Button
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Height = DefaultThemeMetrics.AppTileHeight,
                Margin = new Thickness(8),
                Padding = new Thickness(0),
                Tag = game,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = content,
                ToolTip = available
                    ? $"Open {game.DisplayName} through {GameLibraryService.GetPlatformDisplayName(game.Platform)} emulation."
                    : $"The saved game file is unavailable: {game.SourcePath}"
            };
            button.Click += Game_Click;
            button.PreviewMouseRightButtonUp += Game_RightClick;
            GamesPanel.Children.Add(button);
        }

        if (gamesOnly)
        {
            StatusText.Text = _games.Count == 0
                ? "No games have been added for this GrevID yet."
                : $"{_games.Count} individual game{(_games.Count == 1 ? string.Empty : "s")} in this GrevID's library.";
        }
    }

    private void Game_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: GameLibraryEntry game } button)
        {
            e.Handled = true;
            button.Focus();
            OpenGameActionMenu(game, button);
        }
    }

    private void OpenGameActionMenu(GameLibraryEntry game, Button originButton)
    {
        CancelControllerAppPress();
        _actionMenuGame = game;
        _actionMenuEntry = null;
        _actionMenuOriginButton = originButton;
        _pendingForceKillSessionId = null;
        AppActionTitleText.Text = game.DisplayName;
        AppActionStateText.Text = $"{GameLibraryService.GetPlatformDisplayName(game.Platform)} game • Open launches it through its emulator.";
        AppActionOpenButton.Visibility = Visibility.Visible;
        AppActionSettingsButton.Visibility = Visibility.Visible;
        AppActionSwitchButton.Visibility = Visibility.Collapsed;
        AppActionRestartButton.Visibility = Visibility.Collapsed;
        AppActionCloseButton.Visibility = Visibility.Collapsed;
        AppActionForceKillButton.Visibility = Visibility.Collapsed;
        AppActionAppKillerButton.Visibility = Visibility.Collapsed;
        AppActionRunningAppsButton.Visibility = Visibility.Collapsed;
        AppActionStoreButton.Visibility = Visibility.Collapsed;
        AppActionOverlay.Visibility = Visibility.Visible;
        FocusFirstActionButton();
        ActionMenuOpened?.Invoke(this, EventArgs.Empty);
    }

    private void Game_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameLibraryEntry game })
        {
            return;
        }

        StatusText.Text = GameLibraryService.IsSourceAvailable(game)
            ? $"Starting {game.DisplayName} through {GameLibraryService.GetPlatformDisplayName(game.Platform)}…"
            : $"Game file missing: {game.SourcePath}";
        GameLaunchRequested?.Invoke(game);
    }

    private void AddGame_Click(object sender, RoutedEventArgs e) =>
        AddGameRequested?.Invoke(this, EventArgs.Empty);
}
