using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Files;
using GrevHome.Games;

namespace GrevHome.Views;

public partial class GameFilePickerView : UserControl
{
    private GamePlatform _platform = GamePlatform.PlayStation2;

    public event EventHandler? HomeRequested;
    public event EventHandler? UpRequested;
    public event EventHandler? CancelRequested;
    public event Action<string>? NavigateRequested;
    public event Action<string>? GameSelected;

    public GameFilePickerView()
    {
        InitializeComponent();
        SetPlatform(GamePlatform.PlayStation2);
    }

    public void SetPlatform(GamePlatform platform)
    {
        _platform = platform;
        var platformName = GameLibraryService.GetPlatformDisplayName(platform);
        HeadingText.Text = $"Choose {platformName} Game";
        DescriptionText.Text =
            $"Choose a supported {platformName} game dump. Grev Home stores the file location only; it does not copy the game into C:\\GrevHome.";
    }

    public void ShowHome(IReadOnlyList<FileHomeLocation> locations)
    {
        PathText.Text = "Game locations";
        UpButton.IsEnabled = false;
        EntriesPanel.Children.Clear();
        foreach (var location in locations)
        {
            AddButton(location.Name, location.Detail, location.Path, isFolder: true);
        }
        StatusText.Text = $"Browse to a {GameLibraryService.GetPlatformDisplayName(_platform)} game file.";
    }

    public void ShowDirectory(string path, IReadOnlyList<FileBrowserEntry> entries, bool canGoUp)
    {
        PathText.Text = path;
        UpButton.IsEnabled = canGoUp;
        EntriesPanel.Children.Clear();
        var extensions = GameLibraryService.GetSupportedExtensions(_platform);

        foreach (var entry in entries.Where(entry =>
                     entry.Kind == FileEntryKind.Folder ||
                     entry.Kind == FileEntryKind.File && extensions.Contains(Path.GetExtension(entry.Path))))
        {
            AddButton(
                entry.Kind == FileEntryKind.Folder ? $"📁 {entry.Name}" : entry.Name,
                entry.Kind == FileEntryKind.Folder ? "Folder" : entry.Detail,
                entry.Path,
                entry.Kind == FileEntryKind.Folder);
        }

        StatusText.Text = EntriesPanel.Children.Count == 0
            ? "No supported game files are visible in this folder."
            : "Select a game file to add it to this player's Grev Home library.";
    }

    public void ShowError(string message) => StatusText.Text = message;

    private void AddButton(string title, string detail, string path, bool isFolder)
    {
        var button = new Button
        {
            Width = 285,
            Height = 116,
            Margin = new Thickness(7),
            Tag = new PickerItem(path, isFolder),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        MaxWidth = 245,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = detail,
                        Margin = new Thickness(0, 7, 0, 0),
                        FontSize = 12,
                        Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                        MaxWidth = 245,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        button.Click += Item_Click;
        EntriesPanel.Children.Add(button);
    }

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PickerItem item }) return;
        if (item.IsFolder) NavigateRequested?.Invoke(item.Path);
        else GameSelected?.Invoke(item.Path);
    }

    private void Home_Click(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(this, EventArgs.Empty);
    private void Up_Click(object sender, RoutedEventArgs e) => UpRequested?.Invoke(this, EventArgs.Empty);
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private sealed record PickerItem(string Path, bool IsFolder);
}
