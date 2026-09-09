using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Files;

namespace GrevHome.Views;

/// <summary>
/// Controller-first folder chooser built on the same FileSystemService used by Grev Home Files.
/// It deliberately exposes folders/drives only: A opens a folder, Use This Folder confirms the
/// current directory, and B walks back through parents before cancelling from Files Home.
/// </summary>
public partial class FolderPickerView : UserControl
{
    private readonly FileSystemService _fileSystem = new();
    private readonly Stack<string?> _history = new();
    private string _grevHomeRoot = string.Empty;
    private string? _currentPath;

    public event Action<string>? FolderSelected;
    public event EventHandler? Cancelled;

    public bool IsOpen => Visibility == Visibility.Visible;

    public FolderPickerView()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Open(string heading, string grevHomeRoot, string? initialPath = null)
    {
        _grevHomeRoot = grevHomeRoot;
        _history.Clear();
        HeadingText.Text = heading;
        Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            _currentPath = Path.GetFullPath(initialPath);
            RenderDirectory();
        }
        else
        {
            _currentPath = null;
            RenderHome();
        }

        FocusInitial();
    }

    public bool HandleBack()
    {
        if (!IsOpen) return false;

        if (_history.Count > 0)
        {
            _currentPath = _history.Pop();
            if (_currentPath is null) RenderHome();
            else RenderDirectory();
            FocusInitial();
            return true;
        }

        if (_currentPath is not null)
        {
            var parent = _fileSystem.GetParent(_currentPath);
            if (parent is not null)
            {
                _currentPath = parent;
                RenderDirectory();
                FocusInitial();
                return true;
            }

            _currentPath = null;
            RenderHome();
            FocusInitial();
            return true;
        }

        Cancel();
        return true;
    }

    public void Cancel()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        _history.Clear();
        _currentPath = null;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void RenderHome()
    {
        LocationText.Text = "Files Home";
        EntriesPanel.Children.Clear();
        UseFolderButton.IsEnabled = false;
        UpButton.IsEnabled = false;

        IReadOnlyList<FileHomeLocation> locations;
        try
        {
            locations = _fileSystem.GetHomeLocations(_grevHomeRoot)
                .Where(location => location.Name is not "Test Area")
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not load folders: {ex.Message}";
            locations = Array.Empty<FileHomeLocation>();
        }

        foreach (var location in locations)
        {
            var button = CreateFolderButton(location.Name, location.Detail, location.Path, location.Kind);
            button.Click += (_, _) => Navigate(location.Path);
            EntriesPanel.Children.Add(button);
        }

        EmptyText.Text = "No folders or drives are available.";
        EmptyText.Visibility = locations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "Choose a location or drive. B cancels from Files Home.";
    }

    private void RenderDirectory()
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            RenderHome();
            return;
        }

        try
        {
            var entries = _fileSystem.GetEntries(_currentPath)
                .Where(entry => entry.Kind == FileEntryKind.Folder)
                .ToArray();

            LocationText.Text = _currentPath;
            EntriesPanel.Children.Clear();
            UseFolderButton.IsEnabled = true;
            UpButton.IsEnabled = true;

            foreach (var entry in entries)
            {
                var button = CreateFolderButton(entry.Name, entry.Detail, entry.Path, entry.Kind);
                button.Click += (_, _) => Navigate(entry.Path);
                EntriesPanel.Children.Add(button);
            }

            EmptyText.Text = "This folder has no subfolders. You can still choose Use This Folder.";
            EmptyText.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = "A opens a folder. Use This Folder selects the location shown above.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            StatusText.Text = $"Windows could not open that folder: {ex.Message}";
        }
    }

    private static Button CreateFolderButton(string name, string detail, string path, FileEntryKind kind)
    {
        return new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 68,
            Padding = new Thickness(14, 10, 14, 10),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = kind == FileEntryKind.Drive ? $"DRIVE  •  {name}" : name,
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = $"{detail}  •  {path}",
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = System.Windows.Media.Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private void Navigate(string path)
    {
        if (!Directory.Exists(path))
        {
            StatusText.Text = "That folder is no longer available.";
            return;
        }

        _history.Push(_currentPath);
        _currentPath = Path.GetFullPath(path);
        RenderDirectory();
        FocusInitial();
    }

    private void FocusInitial()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (UseFolderButton.IsEnabled) UseFolderButton.Focus();
            else EntriesPanel.Children.OfType<Button>().FirstOrDefault()?.Focus() ?? CancelButton.Focus();
        }));
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is not null) _history.Push(_currentPath);
        _currentPath = null;
        RenderHome();
        FocusInitial();
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null)
        {
            RenderHome();
            return;
        }

        var parent = _fileSystem.GetParent(_currentPath);
        if (parent is null)
        {
            _history.Push(_currentPath);
            _currentPath = null;
            RenderHome();
        }
        else
        {
            _history.Push(_currentPath);
            _currentPath = parent;
            RenderDirectory();
        }
        FocusInitial();
    }

    private void UseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath)) return;
        var selected = _currentPath;
        Visibility = Visibility.Collapsed;
        _history.Clear();
        _currentPath = null;
        FolderSelected?.Invoke(selected);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();
}
