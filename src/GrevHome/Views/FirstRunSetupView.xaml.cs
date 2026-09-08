using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Store.Installers;
using GrevHome.Storage;

namespace GrevHome.Views;

public sealed record FirstRunSetupResult(string GamesRoot, string BiosRoot, bool ScanGamesFolder);

/// <summary>
/// First-run wizard that asks where Grev Home should keep games and BIOS files. Both folders are
/// machine-wide - every local profile, and every app installed through the Grev Store, is pointed at
/// the same two locations rather than each profile getting its own copy. See
/// <see cref="GrevHome.Storage.MachineDefaultsService"/> for the persisted result this feeds.
/// </summary>
public partial class FirstRunSetupView : UserControl
{
    public event Action<FirstRunSetupResult>? ContinueRequested;

    private string _gamesRoot = string.Empty;
    private string _biosRoot = string.Empty;
    private string _defaultGamesRoot = string.Empty;
    private string _defaultBiosRoot = string.Empty;
    private bool _pickingGames;
    private bool _scanGamesFolder = true;

    public bool IsKeyboardOpen => PathKeyboard.IsOpen;

    public FirstRunSetupView()
    {
        InitializeComponent();
        PathKeyboard.Completed += ApplyCustomPath;
    }

    public void CancelKeyboard() => PathKeyboard.Cancel();

    /// <summary>Prepares the wizard for display. The passed-in defaults are whatever a fresh
    /// <see cref="GrevHome.Storage.MachineDefaultsService"/> would resolve to before setup has ever run.</summary>
    public void Reset(string defaultGamesRoot, string defaultBiosRoot)
    {
        _defaultGamesRoot = defaultGamesRoot;
        _defaultBiosRoot = defaultBiosRoot;
        _gamesRoot = defaultGamesRoot;
        _biosRoot = defaultBiosRoot;
        GamesPathText.Text = _gamesRoot;
        BiosPathText.Text = _biosRoot;
        StatusText.Text = string.Empty;
        _scanGamesFolder = true;
        UpdateScanChoice();
        BuildDriveOptions(GamesDriveOptions, "Games", path =>
        {
            _gamesRoot = path;
            GamesPathText.Text = path;
        });
        BuildDriveOptions(BiosDriveOptions, "Bios", path =>
        {
            _biosRoot = path;
            BiosPathText.Text = path;
        });
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
                // A drive with an unusable root path is skipped rather than breaking the whole wizard.
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

    private void GamesUseDefault_Click(object sender, RoutedEventArgs e)
    {
        _gamesRoot = _defaultGamesRoot;
        GamesPathText.Text = _gamesRoot;
    }

    private void BiosUseDefault_Click(object sender, RoutedEventArgs e)
    {
        _biosRoot = _defaultBiosRoot;
        BiosPathText.Text = _biosRoot;
    }

    private void GamesCustom_Click(object sender, RoutedEventArgs e)
    {
        _pickingGames = true;
        PathKeyboard.Open("Games Folder Path", _gamesRoot, 240);
    }

    private void BiosCustom_Click(object sender, RoutedEventArgs e)
    {
        _pickingGames = false;
        PathKeyboard.Open("BIOS Folder Path", _biosRoot, 240);
    }

    private void ApplyCustomPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (_pickingGames)
        {
            _gamesRoot = value;
            GamesPathText.Text = value;
        }
        else
        {
            _biosRoot = value;
            BiosPathText.Text = value;
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gamesRoot) || string.IsNullOrWhiteSpace(_biosRoot))
        {
            StatusText.Text = "Choose both a Games folder and a BIOS folder to continue.";
            return;
        }

        if (TrustedInstallerSupport.PathsEqual(_gamesRoot, _biosRoot))
        {
            StatusText.Text = "Games and BIOS folders must be different locations.";
            return;
        }

        ContinueRequested?.Invoke(new FirstRunSetupResult(_gamesRoot, _biosRoot, _scanGamesFolder));
    }

    private void ToggleScanChoice_Click(object sender, RoutedEventArgs e)
    {
        _scanGamesFolder = !_scanGamesFolder;
        UpdateScanChoice();
    }

    private void UpdateScanChoice() =>
        ScanChoiceButton.Content = _scanGamesFolder
            ? "✓  Scan for games after I create my account"
            : "○  Skip scanning - I'll add games myself";
}
