using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Files;

namespace GrevHome.Views;

public partial class ProfilePhotoPickerView : UserControl
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private string _purposeNoun = "profile photo";

    public event EventHandler? HomeRequested;
    public event EventHandler? UpRequested;
    public event EventHandler? CancelRequested;
    public event Action<string>? NavigateRequested;
    public event Action<string>? PhotoSelected;

    public ProfilePhotoPickerView()
    {
        InitializeComponent();
    }

    public void SetPurpose(string heading, string purposeNoun)
    {
        HeadingText.Text = heading;
        _purposeNoun = string.IsNullOrWhiteSpace(purposeNoun) ? "profile image" : purposeNoun.Trim();
        DescriptionText.Text = $"Choose a PNG, JPG, JPEG, BMP or GIF image. Grev Home copies it into the profile so the original can move later.";
    }

    public void ShowHome(IReadOnlyList<FileHomeLocation> locations)
    {
        PathText.Text = "Image locations";
        UpButton.IsEnabled = false;
        EntriesPanel.Children.Clear();

        foreach (var location in locations)
        {
            AddButton(location.Name, location.Detail, location.Path, isFolder: true);
        }

        StatusText.Text = $"Open Pictures, Downloads, Documents or a drive, then choose an image file for the {_purposeNoun}.";
    }

    public void ShowDirectory(string path, IReadOnlyList<FileBrowserEntry> entries, bool canGoUp)
    {
        PathText.Text = path;
        UpButton.IsEnabled = canGoUp;
        EntriesPanel.Children.Clear();

        foreach (var entry in entries.Where(entry =>
                     entry.Kind == FileEntryKind.Folder ||
                     entry.Kind == FileEntryKind.File && SupportedExtensions.Contains(Path.GetExtension(entry.Path))))
        {
            AddButton(
                entry.Kind == FileEntryKind.Folder ? $"📁 {entry.Name}" : entry.Name,
                entry.Kind == FileEntryKind.Folder ? "Folder" : entry.Detail,
                entry.Path,
                entry.Kind == FileEntryKind.Folder);
        }

        StatusText.Text = EntriesPanel.Children.Count == 0
            ? $"No supported images for the {_purposeNoun} are visible in this folder."
            : $"Folders open inside Grev Home. Select an image file to use it as the pending {_purposeNoun}.";
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
        else PhotoSelected?.Invoke(item.Path);
    }

    private void Home_Click(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(this, EventArgs.Empty);
    private void Up_Click(object sender, RoutedEventArgs e) => UpRequested?.Invoke(this, EventArgs.Empty);
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private sealed record PickerItem(string Path, bool IsFolder);
}
