using System.Windows;
using System.Windows.Controls;
using GrevHome.Presentation;

namespace GrevHome.Views;

public partial class DashboardTileSettingsView : UserControl
{
    private string _color = "#151923";
    public event Action<string, string>? SaveRequested;
    public event EventHandler? ChooseMediaRequested;
    public event Action<string>? ReusableMediaRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler? BackRequested;

    public DashboardTileSettingsView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += value => DisplayNameBox.Text = value;
    }

    public void SetTile(ResolvedDashboardTile tile, string owner, IReadOnlyList<string> reusable)
    {
        IdentityText.Text = $"{tile.DisplayName} • Home destination • {owner}";
        DisplayNameBox.Text = tile.DisplayName;
        _color = tile.TileColor;
        ColorText.Text = $"Selected colour: {_color}";
        MediaStatusText.Text = tile.TileMediaPath is null ? "No custom full-button artwork configured." : "Custom full-button artwork configured.";
        StatusText.Text = string.Empty;
        ReusablePanel.Children.Clear();
        NoReusableText.Visibility = reusable.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var path in reusable)
        {
            var button = new Button { Width = 112, Height = 76, Margin = new Thickness(0,0,8,8), Padding = new Thickness(4), Tag = path, Content = AppArtworkFactory.CreateTransparent(path, 62) };
            button.Click += (_, _) => ReusableMediaRequested?.Invoke(path);
            ReusablePanel.Children.Add(button);
        }
    }

    public void ShowStatus(string message) => StatusText.Text = message;
    private void Keyboard_Click(object sender, RoutedEventArgs e) => KeyboardOverlay.Open("Enter Home Button Name", DisplayNameBox.Text, 100);
    private void Color_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string color }) { _color = color; ColorText.Text = $"Selected colour: {_color}"; } }
    private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(DisplayNameBox.Text, _color);
    private void ChooseMedia_Click(object sender, RoutedEventArgs e) => ChooseMediaRequested?.Invoke(this, EventArgs.Empty);
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
