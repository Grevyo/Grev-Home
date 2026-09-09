using System.Windows;
using System.Windows.Controls;
using GrevHome.Games;
using GrevHome.Presentation;

namespace GrevHome.Views;

public partial class GameSettingsView : UserControl
{
    private GameLibraryEntry? _game;
    private string _tileColor = GameArtworkFactory.DefaultTileColor;
    private GameConsoleLogoPosition _logoPosition = GameConsoleLogoPosition.TopLeft;
    private bool _logoHasBackground;
    private string _logoBackgroundColor = "#000000";
    private double _logoScale = 1.0;

    public event Action<string>? SaveNameRequested;
    public event EventHandler? ChooseIconRequested;
    public event EventHandler? ChooseTileRequested;
    public event EventHandler? ChooseBackgroundRequested;
    public event Action<string>? ReusableIconRequested;
    public event EventHandler? ResetRequested;
    public event Action<GamePresentationLayout>? SaveLayoutRequested;
    public event EventHandler? BackRequested;

    public GameSettingsView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += value => DisplayNameBox.Text = value;
    }

    public void SetGame(GameLibraryEntry game, string ownerName, string grevId, IReadOnlyList<string> reusableIcons)
    {
        _game = game;
        IdentityText.Text = $"{game.DisplayName} • {GameLibraryService.GetPlatformDisplayName(game.Platform)} • {ownerName} • {grevId}";
        DisplayNameBox.Text = game.DisplayName;
        IconStatusText.Text = string.IsNullOrWhiteSpace(game.IconPath) ? "Showing the console name as text." : "Custom console logo configured for this GrevID.";
        TileStatusText.Text = string.IsNullOrWhiteSpace(game.TileMediaPath) ? "No custom full tile configured." : "Custom full tile configured for this GrevID.";
        BackgroundStatusText.Text = string.IsNullOrWhiteSpace(game.BackgroundMediaPath) ? "Using the full-tile artwork when available." : "Custom dashboard background configured for this GrevID.";
        _tileColor = string.IsNullOrWhiteSpace(game.TileColor) ? GameArtworkFactory.DefaultTileColor : game.TileColor;
        _logoPosition = game.ConsoleLogoPosition;
        _logoHasBackground = game.ConsoleLogoHasBackground;
        _logoBackgroundColor = string.IsNullOrWhiteSpace(game.ConsoleLogoBackgroundColor) ? "#000000" : game.ConsoleLogoBackgroundColor;
        _logoScale = Math.Clamp(game.ConsoleLogoScale, 0.5, 2.5);
        UpdateLayoutLabels();
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
    private void OpenNameKeyboard_Click(object sender, RoutedEventArgs e) =>
        KeyboardOverlay.Open("Enter Game Name", DisplayNameBox.Text, 100);
    private void ChooseIcon_Click(object sender, RoutedEventArgs e) => ChooseIconRequested?.Invoke(this, EventArgs.Empty);
    private void ChooseTile_Click(object sender, RoutedEventArgs e) => ChooseTileRequested?.Invoke(this, EventArgs.Empty);
    private void ChooseBackground_Click(object sender, RoutedEventArgs e) => ChooseBackgroundRequested?.Invoke(this, EventArgs.Empty);
    private void ReusableIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string iconPath }) ReusableIconRequested?.Invoke(iconPath);
    }
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, EventArgs.Empty);
    private void LogoPosition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && Enum.TryParse(value, out GameConsoleLogoPosition position))
        {
            _logoPosition = position;
            UpdateLayoutLabels();
        }
    }
    private void LogoBackground_Click(object sender, RoutedEventArgs e)
    {
        _logoHasBackground = !_logoHasBackground;
        UpdateLayoutLabels();
    }
    private void LogoBackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) _logoBackgroundColor = color;
        UpdateLayoutLabels();
    }
    private void TileColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) _tileColor = color;
        UpdateLayoutLabels();
    }
    private void LogoSmaller_Click(object sender, RoutedEventArgs e)
    {
        _logoScale = Math.Max(0.5, _logoScale - 0.25);
        UpdateLayoutLabels();
    }
    private void LogoLarger_Click(object sender, RoutedEventArgs e)
    {
        _logoScale = Math.Min(2.5, _logoScale + 0.25);
        UpdateLayoutLabels();
    }
    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        SaveLayoutRequested?.Invoke(new GamePresentationLayout(
            _tileColor,
            _logoPosition,
            _logoHasBackground,
            _logoBackgroundColor,
            _logoScale));
    }
    private void UpdateLayoutLabels()
    {
        LogoPositionText.Text = $"Selected: {FormatPosition(_logoPosition)}";
        LogoBackgroundButton.Content = _logoHasBackground ? "Console Logo Background: ON" : "Console Logo Background: OFF";
        LogoBackgroundColorText.Text = $"Selected background: {_logoBackgroundColor}";
        LogoScaleText.Text = $"Scale: {Math.Round(_logoScale * 100):0}%";
        TileColorText.Text = $"Selected tile colour: {_tileColor}";
    }
    private static string FormatPosition(GameConsoleLogoPosition position) => position switch
    {
        GameConsoleLogoPosition.TopLeft => "Top Left",
        GameConsoleLogoPosition.TopCenter => "Top Centre",
        GameConsoleLogoPosition.TopRight => "Top Right",
        GameConsoleLogoPosition.BottomLeft => "Bottom Left",
        GameConsoleLogoPosition.BottomCenter => "Bottom Centre",
        GameConsoleLogoPosition.BottomRight => "Bottom Right",
        _ => "Top Left"
    };
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
