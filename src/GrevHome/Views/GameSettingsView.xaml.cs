using System.Windows;
using System.Windows.Controls;
using GrevHome.Games;
using GrevHome.Presentation;

namespace GrevHome.Views;

public partial class GameSettingsView : UserControl
{
    private GameLibraryEntry? _game;

    public event Action<string>? SaveNameRequested;
    public event EventHandler? ChooseIconRequested;
    public event EventHandler? ChooseTileRequested;
    public event Action<string>? ReusableIconRequested;
    public event EventHandler? ResetRequested;
    public event Action<GamePresentationLayout>? SaveLayoutRequested;
    public event EventHandler? BackRequested;

    public GameSettingsView()
    {
        InitializeComponent();
        LogoPositionBox.ItemsSource = Enum.GetValues<GameConsoleLogoPosition>();
    }

    public void SetGame(GameLibraryEntry game, string ownerName, string grevId, IReadOnlyList<string> reusableIcons)
    {
        _game = game;
        IdentityText.Text = $"{game.DisplayName} • {GameLibraryService.GetPlatformDisplayName(game.Platform)} • {ownerName} • {grevId}";
        DisplayNameBox.Text = game.DisplayName;
        IconStatusText.Text = string.IsNullOrWhiteSpace(game.IconPath) ? "Showing the console name as text." : "Custom console logo configured for this GrevID.";
        TileStatusText.Text = string.IsNullOrWhiteSpace(game.TileMediaPath) ? "No custom full tile configured." : "Custom full tile configured for this GrevID.";
        TileColorBox.Text = string.IsNullOrWhiteSpace(game.TileColor) ? GameArtworkFactory.DefaultTileColor : game.TileColor;
        LogoPositionBox.SelectedItem = game.ConsoleLogoPosition;
        LogoBackgroundCheckBox.IsChecked = game.ConsoleLogoHasBackground;
        LogoBackgroundColorBox.Text = string.IsNullOrWhiteSpace(game.ConsoleLogoBackgroundColor) ? "#000000" : game.ConsoleLogoBackgroundColor;
        LogoScaleSlider.Value = Math.Clamp(game.ConsoleLogoScale, 0.5, 2.5);
        SavedIconsPanel.Children.Clear();
        NoSavedIconsText.Visibility = reusableIcons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var iconPath in reusableIcons)
        {
            var button = new Button
            {
                Width = 92,
                Height = 76,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(5),
                Tag = iconPath,
                Content = AppArtworkFactory.CreateTransparent(iconPath, 58),
                ToolTip = "Use this saved console logo for the current game"
            };
            button.Click += ReusableIcon_Click;
            SavedIconsPanel.Children.Add(button);
        }
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void SaveName_Click(object sender, RoutedEventArgs e) => SaveNameRequested?.Invoke(DisplayNameBox.Text);
    private void ChooseIcon_Click(object sender, RoutedEventArgs e) => ChooseIconRequested?.Invoke(this, EventArgs.Empty);
    private void ChooseTile_Click(object sender, RoutedEventArgs e) => ChooseTileRequested?.Invoke(this, EventArgs.Empty);
    private void ReusableIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string iconPath }) ReusableIconRequested?.Invoke(iconPath);
    }
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, EventArgs.Empty);
    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        var position = LogoPositionBox.SelectedItem is GameConsoleLogoPosition selected
            ? selected
            : GameConsoleLogoPosition.TopLeft;
        SaveLayoutRequested?.Invoke(new GamePresentationLayout(
            TileColorBox.Text,
            position,
            LogoBackgroundCheckBox.IsChecked == true,
            LogoBackgroundColorBox.Text,
            LogoScaleSlider.Value));
    }
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
