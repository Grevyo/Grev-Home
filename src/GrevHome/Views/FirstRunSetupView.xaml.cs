using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Store.Installers;
using GrevHome.Storage;

namespace GrevHome.Views;

public sealed record FirstRunSetupResult(
    string GamesRoot,
    IReadOnlyList<string> AdditionalGamesRoots,
    string BiosRoot,
    bool ScanGamesFolder);

/// <summary>
/// First-run wizard that asks where Grev Home should keep games and BIOS files. The primary Games
/// folder is used by Grev-managed installers, while additional Games locations let one library span
/// multiple drives without pretending everything lives under one root. BIOS remains one shared
/// machine-wide location. Custom locations always use Grev Home's controller-first folder picker.
/// </summary>
public partial class FirstRunSetupView : UserControl
{
    public event Action<FirstRunSetupResult>? ContinueRequested;

    private readonly List<string> _additionalGamesRoots = [];
    private string _gamesRoot = string.Empty;
    private string _biosRoot = string.Empty;
    private string _defaultGamesRoot = string.Empty;
    private string _defaultBiosRoot = string.Empty;
    private bool _scanGamesFolder = true;
    private FolderPickerPurpose _pickerPurpose;
    private string _grevHomeRoot = AppPaths.StandardRoot;

    public bool IsFolderPickerOpen => FolderPicker.IsOpen;

    public FirstRunSetupView()
    {
        InitializeComponent();
        FolderPicker.FolderSelected += ApplyPickedFolder;
        FolderPicker.Cancelled += (_, _) => SetupContent.IsEnabled = true;
    }

    public bool HandleBack()
    {
        if (!FolderPicker.IsOpen) return false;
        return FolderPicker.HandleBack();
    }

    /// <summary>Prepares the wizard for display. The passed-in defaults are whatever a fresh
    /// <see cref="MachineDefaultsService"/> would resolve to before setup has ever run.</summary>
    public void Reset(string defaultGamesRoot, string defaultBiosRoot)
    {
        _defaultGamesRoot = defaultGamesRoot;
        _defaultBiosRoot = defaultBiosRoot;
        _gamesRoot = defaultGamesRoot;
        _biosRoot = defaultBiosRoot;
        _grevHomeRoot = Directory.GetParent(defaultGamesRoot)?.FullName ?? AppPaths.StandardRoot;
        _additionalGamesRoots.Clear();
        GamesPathText.Text = _gamesRoot;
        BiosPathText.Text = _biosRoot;
        StatusText.Text = string.Empty;
        _scanGamesFolder = true;
        UpdateScanChoice();
        RenderAdditionalGamesRoots();
        BuildDriveOptions(GamesDriveOptions, "Games", path => SetPrimaryGamesRoot(path));
        BuildDriveOptions(BiosDriveOptions, "Bios", path => SetBiosRoot(path));
    }

    public void ShowError(string message) => StatusText.Text = message;

    private static void BuildDriveOptions(Panel host, string subfolder, Action<string> select)
    {
        host.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            string target;
            try
            {
                target = Path.Combine(
                    AppPaths.GetStandardRootForDrive(drive.RootDirectory.FullName),
                    subfolder);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd('\\')
                : $"{drive.Name.TrimEnd('\\')} ({drive.VolumeLabel})";

            var button = new Button
            {
                Content = $"{label} • {TrustedInstallerSupport.FormatBytes(drive.AvailableFreeSpace)} free",
                Height = 50,
                MinWidth = 210,
                Margin = new Thickness(0, 0, 10, 10)
            };
            button.Click += (_, _) => select(target);
            host.Children.Add(button);
        }
    }

    private void GamesUseDefault_Click(object sender, RoutedEventArgs e) => SetPrimaryGamesRoot(_defaultGamesRoot);

    private void BiosUseDefault_Click(object sender, RoutedEventArgs e) => SetBiosRoot(_defaultBiosRoot);

    private void GamesCustom_Click(object sender, RoutedEventArgs e) =>
        OpenFolderPicker(FolderPickerPurpose.PrimaryGames, "Choose Primary Games Folder", _gamesRoot);

    private void GamesAddLocation_Click(object sender, RoutedEventArgs e) =>
        OpenFolderPicker(FolderPickerPurpose.AdditionalGames, "Add Another Games Location", null);

    private void BiosCustom_Click(object sender, RoutedEventArgs e) =>
        OpenFolderPicker(FolderPickerPurpose.Bios, "Choose BIOS Folder", _biosRoot);

    private void OpenFolderPicker(FolderPickerPurpose purpose, string heading, string? initialPath)
    {
        _pickerPurpose = purpose;
        SetupContent.IsEnabled = false;
        FolderPicker.Open(heading, _grevHomeRoot, initialPath);
    }

    private void ApplyPickedFolder(string path)
    {
        SetupContent.IsEnabled = true;
        switch (_pickerPurpose)
        {
            case FolderPickerPurpose.PrimaryGames:
                SetPrimaryGamesRoot(path);
                break;
            case FolderPickerPurpose.AdditionalGames:
                AddGamesRoot(path);
                break;
            case FolderPickerPurpose.Bios:
                SetBiosRoot(path);
                break;
        }
    }

    private void SetPrimaryGamesRoot(string path)
    {
        var normalized = Normalize(path);
        if (TrustedInstallerSupport.PathsEqual(normalized, _biosRoot))
        {
            StatusText.Text = "Games and BIOS folders must be different locations.";
            return;
        }

        _gamesRoot = normalized;
        _additionalGamesRoots.RemoveAll(candidate => TrustedInstallerSupport.PathsEqual(candidate, normalized));
        GamesPathText.Text = normalized;
        RenderAdditionalGamesRoots();
        StatusText.Text = string.Empty;
    }

    private void SetBiosRoot(string path)
    {
        var normalized = Normalize(path);
        if (TrustedInstallerSupport.PathsEqual(normalized, _gamesRoot) ||
            _additionalGamesRoots.Any(candidate => TrustedInstallerSupport.PathsEqual(candidate, normalized)))
        {
            StatusText.Text = "The BIOS folder cannot also be one of your Games locations.";
            return;
        }

        _biosRoot = normalized;
        BiosPathText.Text = normalized;
        StatusText.Text = string.Empty;
    }

    private void AddGamesRoot(string path)
    {
        var normalized = Normalize(path);
        if (TrustedInstallerSupport.PathsEqual(normalized, _gamesRoot) ||
            _additionalGamesRoots.Any(candidate => TrustedInstallerSupport.PathsEqual(candidate, normalized)))
        {
            StatusText.Text = "That Games location is already in the list.";
            return;
        }
        if (TrustedInstallerSupport.PathsEqual(normalized, _biosRoot))
        {
            StatusText.Text = "The BIOS folder cannot also be a Games location.";
            return;
        }

        _additionalGamesRoots.Add(normalized);
        RenderAdditionalGamesRoots();
        StatusText.Text = $"Added Games location: {normalized}";
    }

    private void RenderAdditionalGamesRoots()
    {
        AdditionalGamesRootsPanel.Children.Clear();
        AdditionalGamesHeading.Visibility = _additionalGamesRoots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var path in _additionalGamesRoots.ToArray())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = path,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
            };
            row.Children.Add(text);

            var remove = new Button
            {
                Content = "Remove",
                Width = 110,
                Height = 42,
                Margin = new Thickness(12, 0, 0, 0),
                Tag = path
            };
            remove.Click += (_, _) =>
            {
                if (remove.Tag is string candidate)
                {
                    _additionalGamesRoots.RemoveAll(item => TrustedInstallerSupport.PathsEqual(item, candidate));
                    RenderAdditionalGamesRoots();
                }
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            AdditionalGamesRootsPanel.Children.Add(row);
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gamesRoot) || string.IsNullOrWhiteSpace(_biosRoot))
        {
            StatusText.Text = "Choose both a Games folder and a BIOS folder to continue.";
            return;
        }

        if (TrustedInstallerSupport.PathsEqual(_gamesRoot, _biosRoot) ||
            _additionalGamesRoots.Any(path => TrustedInstallerSupport.PathsEqual(path, _biosRoot)))
        {
            StatusText.Text = "Games and BIOS folders must be different locations.";
            return;
        }

        ContinueRequested?.Invoke(new FirstRunSetupResult(
            _gamesRoot,
            _additionalGamesRoots.ToArray(),
            _biosRoot,
            _scanGamesFolder));
    }

    private void ToggleScanChoice_Click(object sender, RoutedEventArgs e)
    {
        _scanGamesFolder = !_scanGamesFolder;
        UpdateScanChoice();
    }

    private void UpdateScanChoice() =>
        ScanChoiceButton.Content = _scanGamesFolder
            ? "✓  Scan my primary Games folder after I create my account"
            : "○  Skip scanning - I'll add games myself";

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(full);
        return !string.IsNullOrWhiteSpace(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            ? root
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private enum FolderPickerPurpose
    {
        PrimaryGames,
        AdditionalGames,
        Bios
    }
}
