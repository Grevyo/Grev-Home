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
    public event EventHandler? BackRequested;

    public GameSettingsView()
    {
        InitializeComponent();
    }

    public void SetGame(GameLibraryEntry game, string ownerName, string grevId, IReadOnlyList<string> reusableIcons)
    {
        _game = game;
        IdentityText.Text = $"{game.DisplayName} • {GameLibraryService.GetPlatformDisplayName(game.Platform)} • {ownerName} • {grevId}";
        DisplayNameBox.Text = game.DisplayName;
        IconStatusText.Text = string.IsNullOrWhiteSpace(game.IconPath) ? "Using the emulator's default icon." : "Custom icon configured for this GrevID.";
        TileStatusText.Text = string.IsNullOrWhiteSpace(game.TileMediaPath) ? "No custom full tile configured." : "Custom full tile configured for this GrevID.";
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
                Content = AppArtworkFactory.Create(iconPath, 58, 8),
                ToolTip = "Use this saved icon for the current game"
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
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
