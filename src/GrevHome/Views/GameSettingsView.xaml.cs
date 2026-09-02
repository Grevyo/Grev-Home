using System.Windows;
using System.Windows.Controls;
using GrevHome.Games;

namespace GrevHome.Views;

public partial class GameSettingsView : UserControl
{
    private GameLibraryEntry? _game;

    public event Action<string>? SaveNameRequested;
    public event EventHandler? ChooseIconRequested;
    public event EventHandler? ChooseTileRequested;
    public event EventHandler? BackRequested;

    public GameSettingsView()
    {
        InitializeComponent();
    }

    public void SetGame(GameLibraryEntry game, string ownerName, string grevId)
    {
        _game = game;
        IdentityText.Text = $"{game.DisplayName} • {GameLibraryService.GetPlatformDisplayName(game.Platform)} • {ownerName} • {grevId}";
        DisplayNameBox.Text = game.DisplayName;
        IconStatusText.Text = string.IsNullOrWhiteSpace(game.IconPath) ? "Using the emulator's default icon." : "Custom icon configured for this GrevID.";
        TileStatusText.Text = string.IsNullOrWhiteSpace(game.TileMediaPath) ? "No custom full tile configured." : "Custom full tile configured for this GrevID.";
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void SaveName_Click(object sender, RoutedEventArgs e) => SaveNameRequested?.Invoke(DisplayNameBox.Text);
    private void ChooseIcon_Click(object sender, RoutedEventArgs e) => ChooseIconRequested?.Invoke(this, EventArgs.Empty);
    private void ChooseTile_Click(object sender, RoutedEventArgs e) => ChooseTileRequested?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
