using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Files;
using GrevHome.Games;

namespace GrevHome.Views;

public sealed record GameScanSelection(GameScanCandidate Candidate, GamePlatform Platform);

/// <summary>
/// Scan Directory: browse to a folder, scan it for games, then review what was found before any of
/// it is added. Nothing is ever added without passing through the review list, because a wrongly
/// detected console produces a game that cannot launch - so anything the scanner is unsure about
/// arrives switched off, with the console left for the user to choose.
/// </summary>
public partial class GameScanView : UserControl
{
    private sealed class ScanRow
    {
        public required GameScanCandidate Candidate { get; init; }
        public required Button IncludeButton { get; init; }
        public required Button PlatformButton { get; init; }
        public bool Included { get; set; }

        /// <summary>Index into the candidate's alternatives, or -1 when no console is chosen yet.</summary>
        public int PlatformIndex { get; set; } = -1;

        public GamePlatform? Platform =>
            PlatformIndex >= 0 && PlatformIndex < Candidate.Alternatives.Count
                ? Candidate.Alternatives[PlatformIndex]
                : null;
    }

    private readonly List<ScanRow> _rows = new();
    private string? _currentPath;
    private string? _scannedPath;
    private bool _fetchBoxArt = true;

    public event EventHandler? BackRequested;
    public event EventHandler? HomeRequested;
    public event EventHandler? UpRequested;
    public event Action<string>? NavigateRequested;
    public event Action<string>? ScanRequested;
    public event Action<IReadOnlyList<GameScanSelection>, bool>? AddRequested;

    public string? CurrentPath => _currentPath;

    public GameScanView()
    {
        InitializeComponent();
    }

    public void ShowHome(IReadOnlyList<FileHomeLocation> locations)
    {
        _currentPath = null;
        ShowBrowseMode();
        PathText.Text = "Choose where to scan";
        FolderPanel.Children.Clear();
        foreach (var location in locations)
        {
            FolderPanel.Children.Add(CreateFolderButton(location.Name, location.Detail, location.Path));
        }
        StatusText.Text = locations.Count == 0 ? "No drives or folders are available right now." : string.Empty;
    }

    public void ShowDirectory(string path, IReadOnlyList<FileBrowserEntry> entries, bool canGoUp)
    {
        _currentPath = path;
        ShowBrowseMode();
        PathText.Text = path;
        FolderPanel.Children.Clear();

        var folders = entries.Where(entry => entry.Kind == FileEntryKind.Folder).ToArray();
        foreach (var folder in folders)
        {
            FolderPanel.Children.Add(CreateFolderButton(folder.Name, folder.Detail, folder.Path));
        }

        var gameFiles = entries.Count(entry => entry.Kind == FileEntryKind.File);
        StatusText.Text = folders.Length == 0
            ? $"No sub-folders here. {gameFiles} file(s) in this folder - choose Scan This Folder to check them."
            : $"{folders.Length} folder(s) here. Scanning also looks inside every sub-folder.";
        _ = canGoUp;
    }

    public void ShowScanning(string path)
    {
        PathText.Text = path;
        StatusText.Text = "Scanning… this can take a moment on a large collection.";
    }

    public void ShowResults(string scannedPath, GameScanReport report)
    {
        _scannedPath = scannedPath;
        _rows.Clear();
        ResultsPanel.Children.Clear();
        BrowseArea.Visibility = Visibility.Collapsed;
        BrowseActions.Visibility = Visibility.Collapsed;
        ResultsArea.Visibility = Visibility.Visible;
        ResultActions.Visibility = Visibility.Visible;
        PathText.Text = scannedPath;

        foreach (var candidate in report.Candidates)
        {
            ResultsPanel.Children.Add(CreateRow(candidate));
        }

        var needsChoice = report.Candidates.Count(candidate => candidate.Platform is null);
        var parts = new List<string>
        {
            $"{report.Candidates.Count} new game(s) found from {report.FilesInspected} file(s)"
        };
        if (report.AlreadyInLibrary > 0) parts.Add($"{report.AlreadyInLibrary} already in your library");
        if (needsChoice > 0) parts.Add($"{needsChoice} need a console chosen before they can be added");
        if (report.StoppedAtLimit) parts.Add($"stopped after {GameScanService.MaxFilesInspected} files - scan a more specific folder to see the rest");

        StatusText.Text = report.Candidates.Count == 0
            ? "No new games were found in that folder." + (report.AlreadyInLibrary > 0 ? $" {report.AlreadyInLibrary} file(s) are already in your library." : string.Empty)
            : string.Join(" • ", parts);

        UpdateAddButton();
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    /// <summary>
    /// Clears the selection after a batch has been added, so a second press cannot look like it
    /// will add the same games again, and reports what happened.
    /// </summary>
    public void MarkAdded(string summary)
    {
        foreach (var row in _rows)
        {
            row.Included = false;
            RenderRow(row);
        }
        UpdateAddButton();
        StatusText.Text = summary + " Choose Back when you are done.";
    }

    public void ShowError(string message) => StatusText.Text = message;

    private void ShowBrowseMode()
    {
        BrowseArea.Visibility = Visibility.Visible;
        BrowseActions.Visibility = Visibility.Visible;
        ResultsArea.Visibility = Visibility.Collapsed;
        ResultActions.Visibility = Visibility.Collapsed;
    }

    private Button CreateFolderButton(string name, string detail, string path)
    {
        var button = new Button
        {
            Height = 58,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = name, FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = detail,
                        FontSize = 11,
                        Foreground = (Brush)FindResource("MutedBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        button.Click += (_, _) => NavigateRequested?.Invoke(path);
        return button;
    }

    private UIElement CreateRow(GameScanCandidate candidate)
    {
        var includeButton = new Button
        {
            Height = 62,
            MinWidth = 520,
            Margin = new Thickness(0, 0, 8, 6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var platformButton = new Button
        {
            Height = 62,
            Width = 300,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var row = new ScanRow
        {
            Candidate = candidate,
            IncludeButton = includeButton,
            PlatformButton = platformButton
        };

        if (candidate.Platform is { } detected)
        {
            var index = candidate.Alternatives.ToList().IndexOf(detected);
            row.PlatformIndex = index < 0 ? 0 : index;
            row.Included = true;
        }

        includeButton.Click += (_, _) =>
        {
            if (row.Platform is null)
            {
                StatusText.Text = "Choose which console this game is for first.";
                return;
            }
            row.Included = !row.Included;
            RenderRow(row);
            UpdateAddButton();
        };

        platformButton.Click += (_, _) =>
        {
            if (row.Candidate.Alternatives.Count == 0) return;
            row.PlatformIndex = (row.PlatformIndex + 1) % row.Candidate.Alternatives.Count;
            // Choosing a console for a file the scanner was unsure about is a clear signal the
            // user wants it, so it switches itself on rather than needing a second press.
            row.Included = true;
            RenderRow(row);
            UpdateAddButton();
        };

        _rows.Add(row);
        RenderRow(row);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { includeButton, platformButton }
        };
    }

    private void RenderRow(ScanRow row)
    {
        var candidate = row.Candidate;
        var marker = row.Platform is null ? "!" : row.Included ? "✓" : "○";
        var detail = candidate.Confidence switch
        {
            GameScanConfidence.FolderName => "Console taken from the folder name",
            GameScanConfidence.Extension => $"Console taken from the {Path.GetExtension(candidate.SourcePath).ToLowerInvariant()} file type",
            _ => "Several consoles use this file type - choose one"
        };

        row.IncludeButton.Content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = $"{marker}  {candidate.SuggestedName}",
                    FontSize = 17,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = $"{detail}  •  {ShortenPath(candidate.SourcePath)}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        row.PlatformButton.Content = row.Platform is { } platform
            ? GameLibraryService.GetPlatformDisplayName(platform) + (candidate.Alternatives.Count > 1 ? "  ▾" : string.Empty)
            : "Choose console  ▾";
    }

    private string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(_scannedPath)) return path;
        var root = _scannedPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..] : path;
    }

    private void UpdateAddButton()
    {
        var ready = _rows.Count(row => row.Included && row.Platform is not null);
        AddButton.Content = ready == 0 ? "Add Games" : $"Add {ready} Game{(ready == 1 ? string.Empty : "s")}";
        AddButton.IsEnabled = ready > 0;
        BoxArtButton.Content = _fetchBoxArt ? "Box art: On" : "Box art: Off";
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            StatusText.Text = "Open a drive or folder first, then choose Scan This Folder.";
            return;
        }
        ScanRequested?.Invoke(_currentPath);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var selections = _rows
            .Where(row => row.Included && row.Platform is not null)
            .Select(row => new GameScanSelection(row.Candidate, row.Platform!.Value))
            .ToArray();
        if (selections.Length == 0)
        {
            StatusText.Text = "Nothing is selected to add.";
            return;
        }
        AddRequested?.Invoke(selections, _fetchBoxArt);
    }

    private void ToggleBoxArt_Click(object sender, RoutedEventArgs e)
    {
        _fetchBoxArt = !_fetchBoxArt;
        UpdateAddButton();
        StatusText.Text = _fetchBoxArt
            ? "Box art will be downloaded for each added game where a match exists."
            : "Box art will not be downloaded. Games are added without artwork.";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(row => row.Platform is not null))
        {
            row.Included = true;
            RenderRow(row);
        }
        UpdateAddButton();
        var pending = _rows.Count(row => row.Platform is null);
        if (pending > 0)
        {
            StatusText.Text = $"{pending} game(s) still need a console chosen and were left switched off.";
        }
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Included = false;
            RenderRow(row);
        }
        UpdateAddButton();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath)) HomeRequested?.Invoke(this, EventArgs.Empty);
        else NavigateRequested?.Invoke(_currentPath);
    }

    private void Home_Click(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(this, EventArgs.Empty);
    private void Up_Click(object sender, RoutedEventArgs e) => UpRequested?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
