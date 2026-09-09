using GrevHome.Files;
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
    private CancellationTokenSource? _gameScanCancellation;

    /// <summary>
    /// Set during first-run setup when the user asks for their primary Games folder to be scanned.
    /// The scan itself cannot run then - games belong to a GrevID and no profile exists yet - so it
    /// is held here and offered once the first account is actually signed in. Additional configured
    /// Games locations are always available as quick locations on Scan Directory Home.
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
        _gameScanView.CancelScanRequested += (_, _) => _gameScanCancellation?.Cancel();
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
    /// Settings entry point. Opens Scan Directory Home so every configured Games location is shown
    /// together instead of silently favouring only the primary root when a library spans drives.
    /// </summary>
    private void OpenGameScanFromSettings()
    {
        if (_session.PrimaryUser?.GrevId is null)
        {
            _settingsView.ShowGameScanStatus("Sign in with a persistent Primary GrevID to scan for games.");
            return;
        }

        OpenGameScan();
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

    private async void ShowGameScanHome()
    {
        _gameScanCurrentPath = null;
        var locations = new List<FileHomeLocation>();

        try
        {
            var gameRoots = await _machineDefaults.GetGamesRootsAsync();
            for (var index = 0; index < gameRoots.Count; index++)
            {
                var root = gameRoots[index];
                if (!Directory.Exists(root)) continue;
                locations.Add(new FileHomeLocation(
                    index == 0 ? "Primary Games" : $"Games Location {index + 1}",
                    root,
                    index == 0 ? "Configured primary Games folder" : "Configured additional Games folder",
                    FileEntryKind.Folder));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            // The normal Files locations below still leave Scan Directory completely usable.
        }

        try
        {
            foreach (var location in _fileSystem.GetHomeLocations(_paths.Root)
                         .Where(location => location.Name is not "Test Area"))
            {
                if (locations.Any(existing => PathsEqualForScan(existing.Path, location.Path))) continue;
                locations.Add(location);
            }

            _gameScanView.ShowHome(locations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (locations.Count > 0) _gameScanView.ShowHome(locations);
            else _gameScanView.ShowError(ex.Message);
        }
    }

    private static bool PathsEqualForScan(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
        _gameScanCancellation?.Dispose();
        _gameScanCancellation = new CancellationTokenSource();
        try
        {
            _gameScanView.ShowScanning(path);
            var existing = await service.GetForProfileAsync(primary.GrevId);
            var report = await _gameScanService.ScanAsync(path, existing, _gameScanCancellation.Token);
            _gameScanView.ShowResults(path, report);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or InvalidDataException)
        {
            _gameScanView.ShowError($"That folder could not be scanned: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            NavigateGameScanPath(path);
            _gameScanView.ShowStatus("Scan cancelled. Nothing was added.");
        }
        finally
        {
            _gameScanInProgress = false;
            _gameScanCancellation?.Dispose();
            _gameScanCancellation = null;
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
        GameArtworkSearchResult? matchedResult = null;
        try
        {
            downloaded = await _boxArtService.TryDownloadBoxArtAsync(
                selection.Platform,
                selection.Candidate.FileTitle,
                selection.Candidate.SuggestedName,
                staging);
            if (downloaded is null)
            {
                var matches = await _boxArtService.SearchAsync(selection.Platform, selection.Candidate.SuggestedName, maximumResults: 3);
                matchedResult = matches.FirstOrDefault(result => result.MatchScore >= 700);
                if (matchedResult is not null)
                {
                    downloaded = await _boxArtService.TryDownloadResultAsync(matchedResult, staging);
                }
            }
            if (downloaded is null)
            {
                return false;
            }

            await service.SaveCustomAssetAsync(grevId, entry.GameId, GameVisualAssetSlot.TileMedia, downloaded);
            if (matchedResult is not null)
            {
                await service.SaveScrapeDetailsAsync(grevId, entry.GameId, matchedResult);
            }
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

    private async Task AutoScrapeCurrentGameAsync()
    {
        var game = _gameSettingsEntry;
        if (game is null) return;
        if (!GameBoxArtService.IsSupported(game.Platform))
        {
            _gameSettingsView.ShowScrapeBusy(
                $"The built-in artwork catalogue does not cover {GameLibraryService.GetPlatformDisplayName(game.Platform)} yet. Choose your own full tile above instead.");
            return;
        }

        _gameSettingsView.ShowScrapeBusy($"Finding the best match for {game.DisplayName}…");
        _boxArtService ??= new GameBoxArtService();
        try
        {
            var results = await _boxArtService.SearchAsync(game.Platform, game.DisplayName, maximumResults: 5);
            if (_gameSettingsEntry?.GameId != game.GameId) return;
            var best = results.FirstOrDefault();
            if (best is null || best.MatchScore < 300)
            {
                _gameSettingsView.ShowScrapeResults(results, game.DisplayName);
                return;
            }

            await ApplyScrapeResultAsync(best);
        }
        catch (OperationCanceledException)
        {
            _gameSettingsView.ShowScrapeBusy("Artwork search was cancelled.");
        }
    }

    private async Task SearchCurrentGameArtworkAsync(string query)
    {
        var game = _gameSettingsEntry;
        if (game is null || string.IsNullOrWhiteSpace(query))
        {
            _gameSettingsView.ShowScrapeBusy("Enter a game title to search for.");
            return;
        }
        if (!GameBoxArtService.IsSupported(game.Platform))
        {
            _gameSettingsView.ShowScrapeBusy(
                $"The built-in artwork catalogue does not cover {GameLibraryService.GetPlatformDisplayName(game.Platform)} yet. Choose your own full tile above instead.");
            return;
        }

        _gameSettingsView.ShowScrapeBusy($"Searching the internet catalogue for {query}…");
        _boxArtService ??= new GameBoxArtService();
        try
        {
            var results = await _boxArtService.SearchAsync(game.Platform, query);
            if (_gameSettingsEntry?.GameId == game.GameId)
            {
                _gameSettingsView.ShowScrapeResults(results, query);
                FocusRouteSoon();
            }
        }
        catch (OperationCanceledException)
        {
            _gameSettingsView.ShowScrapeBusy("Artwork search was cancelled.");
        }
    }

    private async Task ApplyScrapeResultAsync(GameArtworkSearchResult result)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        var game = _gameSettingsEntry;
        if (service is null || primary?.GrevId is null || game is null) return;

        _boxArtService ??= new GameBoxArtService();
        _gameSettingsView.ShowScrapeBusy($"Downloading artwork for {result.Title}…");
        var staging = Path.Combine(Path.GetTempPath(), "GrevHome", "BoxArt");
        string? downloaded = null;
        try
        {
            downloaded = await _boxArtService.TryDownloadResultAsync(result, staging);
            if (downloaded is null)
            {
                _gameSettingsView.ShowScrapeBusy("That artwork could not be downloaded. Check the connection and try again.");
                return;
            }

            var updated = await service.SaveCustomAssetAsync(
                primary.GrevId, game.GameId, GameVisualAssetSlot.TileMedia, downloaded);
            updated = await service.SaveScrapeDetailsAsync(primary.GrevId, game.GameId, result);

            if (_gameSettingsEntry?.GameId != game.GameId ||
                !string.Equals(_session.PrimaryUser?.GrevId, primary.GrevId, StringComparison.OrdinalIgnoreCase)) return;

            _gameSettingsEntry = updated;
            await RefreshProfileGamesAsync();
            _gameSettingsView.ShowScrapeApplied(result);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or HttpRequestException)
        {
            _gameSettingsView.ShowScrapeBusy($"Artwork could not be applied: {ex.Message}");
        }
        finally
        {
            if (downloaded is not null)
            {
                try { if (File.Exists(downloaded)) File.Delete(downloaded); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
            "You asked during setup to scan for games. This is your primary Games folder - choose Scan This Folder to add what is in it. Your other Games locations are available from Home.");
    }
}
