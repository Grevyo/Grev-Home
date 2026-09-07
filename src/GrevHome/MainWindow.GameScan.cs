using GrevHome.Games;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly GameScanView _gameScanView = new();
    private readonly GameScanService _gameScanService = new();
    private GameBoxArtService? _boxArtService;
    private string? _gameScanCurrentPath;
    private bool _gameScanInProgress;

    /// <summary>
    /// Set during first-run setup when the user asks for their Games folder to be scanned. The scan
    /// itself cannot run then - games belong to a GrevID and no profile exists yet - so it is held
    /// here and offered once the first account is actually signed in.
    /// </summary>
    private string? _pendingFirstRunScanRoot;

    private void InitializeGameScanIntegration()
    {
        _gameAddView.ScanDirectoryRequested += (_, _) => OpenGameScan();
        _settingsView.ScanDirectoryRequested += (_, _) => OpenGameScanFromSettings();

        _gameScanView.BackRequested += (_, _) => _navigation.GoBack();
        _gameScanView.HomeRequested += (_, _) => ShowGameScanHome();
        _gameScanView.UpRequested += (_, _) => NavigateGameScanUp();
        _gameScanView.NavigateRequested += NavigateGameScanPath;
        _gameScanView.ScanRequested += path => _ = RunGameScanAsync(path);
        _gameScanView.AddRequested += (selections, fetchBoxArt) => _ = AddScannedGamesAsync(selections, fetchBoxArt);

        _navigation.RouteChanged += route =>
        {
            if (route == Route.GameScan)
            {
                RouteHost.Content = _gameScanView;
                FocusRouteSoon();
            }
        };
    }

    /// <summary>
    /// Settings entry point. Starts at the machine-wide Games folder chosen during first-run setup,
    /// since that is where games are expected to live.
    /// </summary>
    private void OpenGameScanFromSettings()
    {
        if (_session.PrimaryUser?.GrevId is null)
        {
            _settingsView.ShowGameScanStatus("Sign in with a persistent Primary GrevID to scan for games.");
            return;
        }

        _ = OpenGameScanAtGamesRootAsync();
    }

    private async Task OpenGameScanAtGamesRootAsync()
    {
        string? gamesRoot = null;
        try
        {
            gamesRoot = await _machineDefaults.GetGamesRootAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Falling back to the drive list is always safe; the user can browse anywhere from there.
        }

        OpenGameScan(gamesRoot);
    }

    private void OpenGameScan(string? startAt = null)
    {
        if (_session.PrimaryUser?.GrevId is null)
        {
            _gameAddView.ShowStatus("A persistent Primary GrevID is required to scan for games.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(startAt) && Directory.Exists(startAt))
        {
            NavigateGameScanPath(startAt);
        }
        else
        {
            ShowGameScanHome();
        }

        _navigation.Navigate(Route.GameScan);
    }

    private void ShowGameScanHome()
    {
        _gameScanCurrentPath = null;
        try
        {
            var locations = _fileSystem.GetHomeLocations(_paths.Root)
                .Where(location => location.Name is not "Test Area")
                .ToArray();
            _gameScanView.ShowHome(locations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _gameScanView.ShowError(ex.Message);
        }
    }

    private void NavigateGameScanPath(string path)
    {
        try
        {
            var entries = _fileSystem.GetEntries(path);
            _gameScanCurrentPath = path;
            _gameScanView.ShowDirectory(path, entries, _fileSystem.GetParent(path) is not null);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _gameScanView.ShowError(ex.Message);
        }
    }

    private void NavigateGameScanUp()
    {
        if (string.IsNullOrWhiteSpace(_gameScanCurrentPath))
        {
            ShowGameScanHome();
            return;
        }

        var parent = _fileSystem.GetParent(_gameScanCurrentPath);
        if (parent is null) ShowGameScanHome();
        else NavigateGameScanPath(parent);
    }

    private async Task RunGameScanAsync(string path)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _gameScanInProgress)
        {
            return;
        }

        _gameScanInProgress = true;
        try
        {
            _gameScanView.ShowScanning(path);
            var existing = await service.GetForProfileAsync(primary.GrevId);
            var report = await _gameScanService.ScanAsync(path, existing);
            _gameScanView.ShowResults(path, report);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or InvalidDataException)
        {
            _gameScanView.ShowError($"That folder could not be scanned: {ex.Message}");
        }
        finally
        {
            _gameScanInProgress = false;
        }
    }

    private async Task AddScannedGamesAsync(IReadOnlyList<GameScanSelection> selections, bool fetchBoxArt)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _gameScanInProgress)
        {
            return;
        }

        var grevId = primary.GrevId;
        _gameScanInProgress = true;
        var added = 0;
        var artwork = 0;
        var failed = 0;

        try
        {
            for (var index = 0; index < selections.Count; index++)
            {
                var selection = selections[index];
                _gameScanView.ShowStatus($"Adding {index + 1} of {selections.Count}: {selection.Candidate.SuggestedName}…");

                GameLibraryEntry entry;
                try
                {
                    entry = await service.AddAsync(
                        grevId,
                        selection.Platform,
                        selection.Candidate.SourcePath,
                        selection.Candidate.SuggestedName);
                    added++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                               or ArgumentException or InvalidOperationException or FileNotFoundException)
                {
                    // One unreadable or unsupported file must not abandon the rest of the batch.
                    failed++;
                    continue;
                }

                if (fetchBoxArt && await TryApplyBoxArtAsync(service, grevId, entry, selection))
                {
                    artwork++;
                }
            }

            await RefreshProfileGamesAsync();

            var summary = $"Added {added} game(s) to {primary.DisplayName}'s library.";
            if (artwork > 0) summary += $" Box art was found for {artwork}.";
            if (failed > 0) summary += $" {failed} could not be added and were skipped.";

            // Stay on the scan screen so the result is actually read, rather than navigating away
            // and leaving the summary on a page the user is no longer looking at. The library and
            // Home already hold the new games by this point.
            _gameScanView.MarkAdded(summary);
            _installedLibraryView.ShowGameStatus(summary);
        }
        finally
        {
            _gameScanInProgress = false;
        }
    }

    private async Task<bool> TryApplyBoxArtAsync(
        GameLibraryService service,
        string grevId,
        GameLibraryEntry entry,
        GameScanSelection selection)
    {
        if (!GameBoxArtService.IsSupported(selection.Platform))
        {
            return false;
        }

        _boxArtService ??= new GameBoxArtService();
        var staging = Path.Combine(Path.GetTempPath(), "GrevHome", "BoxArt");
        string? downloaded = null;
        try
        {
            downloaded = await _boxArtService.TryDownloadBoxArtAsync(
                selection.Platform,
                selection.Candidate.FileTitle,
                selection.Candidate.SuggestedName,
                staging);
            if (downloaded is null)
            {
                return false;
            }

            await service.SaveCustomAssetAsync(grevId, entry.GameId, GameVisualAssetSlot.TileMedia, downloaded);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or HttpRequestException)
        {
            // Artwork is a bonus. Losing it never invalidates a game that was added successfully.
            return false;
        }
        finally
        {
            if (downloaded is not null)
            {
                try
                {
                    if (File.Exists(downloaded)) File.Delete(downloaded);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A stray temp file in the OS temp folder is harmless.
                }
            }
        }
    }

    /// <summary>
    /// Runs the scan the user opted into during first-run setup, once a real profile exists to own
    /// the games. Called after sign-in; it only ever prompts, never adds anything on its own.
    /// </summary>
    private void OfferPendingFirstRunScan()
    {
        var root = _pendingFirstRunScanRoot;
        if (string.IsNullOrWhiteSpace(root) || _session.PrimaryUser?.GrevId is null)
        {
            return;
        }

        _pendingFirstRunScanRoot = null;
        if (!Directory.Exists(root))
        {
            return;
        }

        OpenGameScan(root);
        _gameScanView.ShowStatus(
            "You asked during setup to scan for games. This is your Games folder - choose Scan This Folder to add what is in it.");
    }
}
