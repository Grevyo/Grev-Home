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

    public bool IsModalOpen => KeyboardOverlay.IsOpen || DeleteOverlay.Visibility == Visibility.Visible;

    public FileExplorerView()
    {
        InitializeComponent();
        ClearSelection();

        // Files deliberately reuses the one Grev Home controller keyboard. The old Files-only
        // hand-built keyboard has been removed so keyboard behaviour cannot drift between pages.
        KeyboardOverlay.Completed += value =>
            NameRequested?.Invoke(new FileNameRequest(_editorMode, value, _editorSourcePath));
        KeyboardOverlay.Cancelled += (_, _) =>
            StatusText.Text = "Rename/create action cancelled.";
        KeyboardOverlay.Opened += (_, _) => ModalOpened?.Invoke(this, EventArgs.Empty);
        KeyboardOverlay.Closed += (_, _) => ModalClosed?.Invoke(this, EventArgs.Empty);
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

    public void ShowEditorError(string message) =>
        StatusText.Text = $"Rename/create failed: {message}";

    public void CloseEditor(string message)
    {
        if (KeyboardOverlay.IsOpen)
        {
            // Programmatic completion is silent here; the normal Done/Cancel path already raises
            // KeyboardOverlay.Closed and therefore owns the matching modal-history pop.
            KeyboardOverlay.Visibility = Visibility.Collapsed;
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
        // Used while navigation is already performing the modal Back transition. Hide the shared
        // keyboard directly so its Closed event cannot create a second history pop.
        KeyboardOverlay.Visibility = Visibility.Collapsed;
        DeleteOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Gives every Files modal an explicit controller landing target. The shared keyboard owns its
    /// own first-key focus. Delete intentionally starts on Cancel.
    /// </summary>
    public void RefocusModal()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DeleteOverlay.Visibility == Visibility.Visible)
            {
                DeleteCancelButton.Focus();
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
        KeyboardOverlay.Open(
            mode == FileNameEditorMode.CreateFolder ? "New Folder" : "Rename Item",
            initialName,
            maxLength: 120);
    }

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
