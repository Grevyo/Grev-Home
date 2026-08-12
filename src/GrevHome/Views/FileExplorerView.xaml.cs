using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Files;

namespace GrevHome.Views;

public enum FileNameEditorMode
{
    CreateFolder,
    Rename
}

public sealed record FileNameRequest(FileNameEditorMode Mode, string Name, string? SourcePath);

public partial class FileExplorerView : UserControl
{
    private FileBrowserEntry? _selectedEntry;
    private FileHomeLocation? _selectedHomeLocation;
    private FileNameEditorMode _editorMode;
    private string? _editorSourcePath;
    private bool _lowercase;

    public event EventHandler? BackToDashboardRequested;
    public event EventHandler? HomeRequested;
    public event EventHandler? UpRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? ModalOpened;
    public event EventHandler? ModalClosed;
    public event Action<string>? NavigateRequested;
    public event Action<FileNameRequest>? NameRequested;
    public event Action<string>? DeleteRequested;
    public event Action<string>? CopyRequested;
    public event Action<string>? MoveRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? CancelTransferRequested;

    public bool IsModalOpen => EditorOverlay.Visibility == Visibility.Visible || DeleteOverlay.Visibility == Visibility.Visible;

    public FileExplorerView()
    {
        InitializeComponent();
        BuildKeyboard();
        ClearSelection();
    }

    public void SetHome(IReadOnlyList<FileHomeLocation> locations, FileTransferRequest? transfer)
    {
        LocationText.Text = "Files Home";
        EntriesPanel.Children.Clear();
        ClearSelection();

        foreach (var location in locations)
        {
            EntriesPanel.Children.Add(CreateHomeLocationButton(location));
        }

        EmptyText.Visibility = locations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = locations.Count == 0 ? "No locations are available." : string.Empty;
        UpButton.IsEnabled = false;
        NewFolderButton.IsEnabled = false;
        SetTransfer(transfer, canPaste: false);
        StatusText.Text = "Select a location or drive. B returns to Dashboard from Files Home.";
    }

    public void SetDirectory(
        string directoryPath,
        IReadOnlyList<FileBrowserEntry> entries,
        bool hasParent,
        FileTransferRequest? transfer)
    {
        LocationText.Text = directoryPath;
        EntriesPanel.Children.Clear();
        ClearSelection();

        foreach (var entry in entries)
        {
            EntriesPanel.Children.Add(CreateEntryButton(entry));
        }

        EmptyText.Text = "This folder is empty.";
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpButton.IsEnabled = hasParent;
        NewFolderButton.IsEnabled = true;
        SetTransfer(transfer, canPaste: true);
        StatusText.Text = "Select an item, then choose an action. B goes to the parent folder.";
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    public void ShowEditorError(string message) => EditorStatusText.Text = message;

    public void CloseEditor(string message)
    {
        if (EditorOverlay.Visibility == Visibility.Visible)
        {
            EditorOverlay.Visibility = Visibility.Collapsed;
            ModalClosed?.Invoke(this, EventArgs.Empty);
        }

        StatusText.Text = message;
    }

    public void CloseDelete(string message)
    {
        if (DeleteOverlay.Visibility == Visibility.Visible)
        {
            DeleteOverlay.Visibility = Visibility.Collapsed;
            ModalClosed?.Invoke(this, EventArgs.Empty);
        }

        StatusText.Text = message;
    }

    public void CloseModals()
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        DeleteOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Gives every Files modal an explicit controller landing target. Delete intentionally starts
    /// on Cancel; rename/create starts on the first on-screen keyboard key rather than allowing the
    /// underlying Files toolbar to keep focus behind the overlay.
    /// </summary>
    public void RefocusModal()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DeleteOverlay.Visibility == Visibility.Visible)
            {
                DeleteCancelButton.Focus();
                return;
            }

            if (EditorOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            var firstKey = NameKeyboard.Children.OfType<Button>()
                .FirstOrDefault(button => button.IsVisible && button.IsEnabled && button.Focusable);
            if (firstKey is not null)
            {
                firstKey.Focus();
            }
            else
            {
                EditorCancelButton.Focus();
            }
        }));
    }

    private Button CreateHomeLocationButton(FileHomeLocation location)
    {
        var button = CreateEntryShell(location.Name, location.Detail, location.Kind);
        button.Tag = location;
        button.Click += (_, _) => SelectHomeLocation(location);
        return button;
    }

    private Button CreateEntryButton(FileBrowserEntry entry)
    {
        var modified = entry.ModifiedAt.HasValue
            ? $" • {entry.ModifiedAt.Value.ToLocalTime():g}"
            : string.Empty;
        var button = CreateEntryShell(entry.Name, entry.Detail + modified, entry.Kind);
        button.Tag = entry;
        button.Click += (_, _) => SelectEntry(entry);
        return button;
    }

    private Button CreateEntryShell(string name, string detail, FileEntryKind kind)
    {
        return new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 68,
            Padding = new Thickness(14, 10, 14, 10),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(92) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    BuildKindLabel(kind),
                    BuildEntryDetails(name, detail)
                }
            }
        };
    }

    private static TextBlock BuildKindLabel(FileEntryKind kind)
    {
        var text = new TextBlock
        {
            Text = kind switch
            {
                FileEntryKind.Drive => "DRIVE",
                FileEntryKind.Folder => "FOLDER",
                _ => "FILE"
            },
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 0);
        return text;
    }

    private TextBlock BuildEntryDetails(string name, string detail)
    {
        var text = new TextBlock
        {
            Text = $"{name}\n{detail}",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        return text;
    }

    private void SelectHomeLocation(FileHomeLocation location)
    {
        _selectedEntry = null;
        _selectedHomeLocation = location;
        SelectedNameText.Text = location.Name;
        SelectedDetailText.Text = $"{location.Detail} • {location.Path}";
        OpenButton.IsEnabled = true;
        CopyButton.IsEnabled = false;
        MoveButton.IsEnabled = false;
        RenameButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    private void SelectEntry(FileBrowserEntry entry)
    {
        _selectedHomeLocation = null;
        _selectedEntry = entry;
        SelectedNameText.Text = entry.Name;
        SelectedDetailText.Text = $"{entry.Detail} • {entry.Path}";
        OpenButton.IsEnabled = entry.Kind is FileEntryKind.Folder or FileEntryKind.Drive;
        CopyButton.IsEnabled = true;
        MoveButton.IsEnabled = true;
        RenameButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
    }

    private void ClearSelection()
    {
        _selectedEntry = null;
        _selectedHomeLocation = null;
        SelectedNameText.Text = "No item selected";
        SelectedDetailText.Text = "Choose an item, then use the actions on the right.";
        OpenButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        MoveButton.IsEnabled = false;
        RenameButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    private void SetTransfer(FileTransferRequest? transfer, bool canPaste)
    {
        if (transfer is null)
        {
            TransferText.Visibility = Visibility.Collapsed;
            PasteButton.IsEnabled = false;
            CancelTransferButton.IsEnabled = false;
            return;
        }

        TransferText.Visibility = Visibility.Visible;
        TransferText.Text = $"{transfer.Mode}: {transfer.SourcePath}";
        PasteButton.IsEnabled = canPaste;
        CancelTransferButton.IsEnabled = true;
    }

    private void BeginEditor(FileNameEditorMode mode, string initialName, string? sourcePath)
    {
        _editorMode = mode;
        _editorSourcePath = sourcePath;
        _lowercase = false;
        UpdateKeyboardCase();
        NameTextBox.Text = initialName;
        NameTextBox.CaretIndex = NameTextBox.Text.Length;
        EditorTitleText.Text = mode == FileNameEditorMode.CreateFolder ? "New Folder" : "Rename Item";
        EditorStatusText.Text = "Use the controller keyboard or a physical keyboard.";
        EditorOverlay.Visibility = Visibility.Visible;
        ModalOpened?.Invoke(this, EventArgs.Empty);
    }

    private void BuildKeyboard()
    {
        const string keys = "QWERTYUIOPASDFGHJKLZXCVBNM1234567890-_.()";
        foreach (var key in keys)
        {
            var button = new Button
            {
                Content = key.ToString(),
                Tag = key,
                Height = 46,
                Margin = new Thickness(3),
                FontSize = 16
            };
            button.Click += NameKey_Click;
            NameKeyboard.Children.Add(button);
        }
    }

    private void UpdateKeyboardCase()
    {
        foreach (var button in NameKeyboard.Children.OfType<Button>())
        {
            if (button.Tag is not char original)
            {
                continue;
            }

            button.Content = char.IsLetter(original)
                ? (_lowercase ? char.ToLowerInvariant(original) : char.ToUpperInvariant(original)).ToString()
                : original.ToString();
        }

        CaseButton.Content = _lowercase ? "aA" : "Aa";
    }

    private void NameKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: char original } || NameTextBox.Text.Length >= NameTextBox.MaxLength)
        {
            return;
        }

        var value = char.IsLetter(original) && _lowercase
            ? char.ToLowerInvariant(original)
            : original;
        NameTextBox.Text += value;
        NameTextBox.CaretIndex = NameTextBox.Text.Length;
    }

    private void ToggleCase_Click(object sender, RoutedEventArgs e)
    {
        _lowercase = !_lowercase;
        UpdateKeyboardCase();
    }

    private void NameSpace_Click(object sender, RoutedEventArgs e)
    {
        if (NameTextBox.Text.Length < NameTextBox.MaxLength)
        {
            NameTextBox.Text += " ";
            NameTextBox.CaretIndex = NameTextBox.Text.Length;
        }
    }

    private void NameBackspace_Click(object sender, RoutedEventArgs e)
    {
        if (NameTextBox.Text.Length == 0)
        {
            return;
        }

        NameTextBox.Text = NameTextBox.Text[..^1];
        NameTextBox.CaretIndex = NameTextBox.Text.Length;
    }

    private void SaveName_Click(object sender, RoutedEventArgs e) =>
        NameRequested?.Invoke(new FileNameRequest(_editorMode, NameTextBox.Text, _editorSourcePath));

    private void CancelEditor_Click(object sender, RoutedEventArgs e) =>
        CloseEditor("Rename/create action cancelled.");

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var path = _selectedHomeLocation?.Path ?? _selectedEntry?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_selectedEntry?.Kind == FileEntryKind.File)
        {
            StatusText.Text = "File launching is intentionally deferred until it can route through Grev Home's app/runtime model.";
            return;
        }

        NavigateRequested?.Invoke(path);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is not null)
        {
            CopyRequested?.Invoke(_selectedEntry.Path);
        }
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is not null)
        {
            MoveRequested?.Invoke(_selectedEntry.Path);
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is not null)
        {
            BeginEditor(FileNameEditorMode.Rename, _selectedEntry.Name, _selectedEntry.Path);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        DeleteTargetText.Text = _selectedEntry.Path;
        DeleteOverlay.Visibility = Visibility.Visible;
        ModalOpened?.Invoke(this, EventArgs.Empty);
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is not null)
        {
            DeleteRequested?.Invoke(_selectedEntry.Path);
        }
    }

    private void CancelDelete_Click(object sender, RoutedEventArgs e) =>
        CloseDelete("Delete cancelled.");

    private void NewFolder_Click(object sender, RoutedEventArgs e) =>
        BeginEditor(FileNameEditorMode.CreateFolder, string.Empty, null);

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteRequested?.Invoke(this, EventArgs.Empty);

    private void CancelTransfer_Click(object sender, RoutedEventArgs e) => CancelTransferRequested?.Invoke(this, EventArgs.Empty);

    private void Home_Click(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(this, EventArgs.Empty);

    private void Up_Click(object sender, RoutedEventArgs e) => UpRequested?.Invoke(this, EventArgs.Empty);

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void BackToDashboard_Click(object sender, RoutedEventArgs e) => BackToDashboardRequested?.Invoke(this, EventArgs.Empty);
}
