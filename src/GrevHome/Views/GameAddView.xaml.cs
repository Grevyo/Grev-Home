using System.Windows;
using System.Windows.Controls;
using GrevHome.Games;

namespace GrevHome.Views;

public partial class GameAddView : UserControl
{
    private GamePlatform _platform = GamePlatform.PlayStation2;

    public event EventHandler? BackRequested;
    public event Action<GamePlatform>? ChooseFileRequested;

    public GameAddView()
    {
        InitializeComponent();
        PopulatePlatformChoices();
        UpdatePresentation();
    }

    private void PopulatePlatformChoices()
    {
        PlatformChoicesPanel.Children.Clear();
        foreach (var platform in Enum.GetValues<GamePlatform>())
        {
            var button = new Button
            {
                Content = GameLibraryService.GetPlatformDisplayName(platform),
                Tag = platform.ToString(),
                Width = 340,
                MinHeight = 46,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += PlatformChoice_Click;
            PlatformChoicesPanel.Children.Add(button);
        }
    }

    public void SetOwner(string displayName, string grevId)
    {
        OwnerText.Text = $"Adding to {displayName} • {grevId}. Games are local to this GrevID's Grev Home library.";
        StatusText.Text = string.Empty;
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void Platform_Click(object sender, RoutedEventArgs e)
    {
        PlatformChoicesPanel.Visibility = PlatformChoicesPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PlatformChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && Enum.TryParse<GamePlatform>(value, true, out var platform))
        {
            _platform = platform;
            PlatformChoicesPanel.Visibility = Visibility.Collapsed;
            UpdatePresentation();
            PlatformButton.Focus();
        }
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e) => ChooseFileRequested?.Invoke(_platform);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void UpdatePresentation()
    {
        var name = GameLibraryService.GetPlatformDisplayName(_platform);
        PlatformButton.Content = $"{name}  ▾";
        PlatformHelpText.Text = _platform switch
        {
            GamePlatform.PlayStation2 =>
                "Choose your own dumped PS2 game image. Grev Home stores its location and launches it through this profile's PCSX2 installation.",
            _ => $"Choose a supported {name} game file. Grev Home launches it through this profile's RetroArch installation and a compatible installed core."
        };
    }
}
