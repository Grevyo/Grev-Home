using System.ComponentModel;
using System.IO;
using System.Windows;
using GrevHome.Games;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly GameAddView _gameAddView = new();
    private readonly GameFilePickerView _gameFilePickerView = new();
    private readonly GameSettingsView _gameSettingsView = new();
    private readonly ProfilePhotoPickerView _gameArtworkPickerView = new();
    private readonly GameLaunchResolver _gameLaunchResolver = new();
    private GameLibraryService? _gameLibraryService;
    private GamePlatform _pendingGamePlatform = GamePlatform.PlayStation2;
    private string? _gameFileCurrentPath;
    private string? _renderedGameLibraryOwnerGrevId;
    private int _gameLibraryRefreshGeneration;
    private bool _gameAddInProgress;
    private bool _gameLibraryIntegrationReady;
    private GameLibraryEntry? _gameSettingsEntry;
    private string? _gameArtworkCurrentPath;
    private GameVisualAssetSlot _pendingGameArtworkSlot;

    private void InitializeGameLibraryIntegration()
    {
        if (_gameLibraryIntegrationReady)
        {
            return;
        }

        _gameLibraryIntegrationReady = true;
        _gameLibraryService = new GameLibraryService(_paths);
        _installedLibraryView.InitializeGameLibraryUi();

        _installedLibraryView.AddGameRequested += (_, _) => OpenGameAdd();
        _installedLibraryView.GameLaunchRequested += game =>
        {
            CloseInstalledLibraryActionMenuForAction();
            _ = LaunchGameAsync(game);
        };
        _installedLibraryView.GameSettingsRequested += game =>
        {
            CloseInstalledLibraryActionMenuForAction();
            OpenGameSettings(game);
        };
        _dashboardView.GameRequested += game => _ = LaunchGameAsync(game);

        _gameSettingsView.BackRequested += (_, _) => _navigation.GoBack();
        _gameSettingsView.SaveNameRequested += name => _ = SaveGameNameAsync(name);
        _gameSettingsView.ChooseIconRequested += (_, _) => OpenGameArtworkPicker(GameVisualAssetSlot.Icon);
        _gameSettingsView.ChooseTileRequested += (_, _) => OpenGameArtworkPicker(GameVisualAssetSlot.TileMedia);
        _gameSettingsView.ReusableIconRequested += path => _ = UseReusableGameIconAsync(path);
        _gameSettingsView.ResetRequested += (_, _) => _ = ResetGamePresentationAsync();
        _gameSettingsView.SaveLayoutRequested += layout => _ = SaveGamePresentationLayoutAsync(layout);

        _gameArtworkPickerView.HomeRequested += (_, _) => ShowGameArtworkHome();
        _gameArtworkPickerView.UpRequested += (_, _) => NavigateGameArtworkUp();
        _gameArtworkPickerView.CancelRequested += (_, _) => _navigation.GoBack();
        _gameArtworkPickerView.NavigateRequested += NavigateGameArtworkPath;
        _gameArtworkPickerView.PhotoSelected += path => _ = SaveSelectedGameArtworkAsync(path);

        _gameAddView.BackRequested += (_, _) => _navigation.GoBack();
        _gameAddView.ChooseFileRequested += OpenGameFilePicker;

        _gameFilePickerView.HomeRequested += (_, _) => ShowGameFileHome();
        _gameFilePickerView.UpRequested += (_, _) => NavigateGameFileUp();
        _gameFilePickerView.CancelRequested += (_, _) => _navigation.GoBack();
        _gameFilePickerView.NavigateRequested += NavigateGameFilePath;
        _gameFilePickerView.GameSelected += path => _ = AddSelectedGameAsync(path);

        _navigation.RouteChanged += route =>
        {
            switch (route)
            {
                case Route.GameAdd:
                    RenderGameAdd();
                    RouteHost.Content = _gameAddView;
                    FocusRouteSoon();
                    break;
                case Route.GameFilePicker:
                    RouteHost.Content = _gameFilePickerView;
                    FocusRouteSoon();
                    break;
                case Route.GameSettings:
                    RouteHost.Content = _gameSettingsView;
                    RenderGameSettings();
                    FocusRouteSoon();
                    break;
                case Route.GameArtworkPicker:
                    RouteHost.Content = _gameArtworkPickerView;
                    FocusRouteSoon();
                    break;
                case Route.InstalledLibrary:
                case Route.Dashboard:
                    _ = RefreshProfileGamesAsync();
                    break;
            }
        };

        _session.Changed += (_, _) =>
        {
            // Never leave one profile's game objects clickable while another Primary GrevID is
            // becoming active. In-flight reads are invalidated and both cached surfaces are cleared
            // synchronously on the UI thread before the replacement library is loaded.
            if (Dispatcher.CheckAccess())
            {
                InvalidateProfileGameLibrary();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(InvalidateProfileGameLibrary));
            }
        };
    }

    private void OpenGameSettings(GameLibraryEntry game)
    {
        if (_session.PrimaryUser?.GrevId is null) return;
        _gameSettingsEntry = game;
        _navigation.Navigate(Route.GameSettings);
    }

    private void RenderGameSettings()
    {
        var primary = _session.PrimaryUser;
        if (_gameSettingsEntry is null || primary?.GrevId is null) return;
        _gameSettingsView.SetGame(
            _gameSettingsEntry,
            primary.DisplayName,
            primary.GrevId,
            _gameLibraryService?.GetReusableIcons(primary.GrevId) ?? Array.Empty<string>());
    }

    private async Task SaveGameNameAsync(string displayName)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _gameSettingsEntry is null) return;
        try
        {
            _gameSettingsEntry = await service.SaveDisplayNameAsync(primary.GrevId, _gameSettingsEntry.GameId, displayName);
            RenderGameSettings();
            await RefreshProfileGamesAsync();
            _gameSettingsView.ShowStatus("Game name saved for this GrevID.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _gameSettingsView.ShowStatus($"Could not save the game name: {ex.Message}");
        }
    }

    private void OpenGameArtworkPicker(GameVisualAssetSlot slot)
    {
        if (_session.PrimaryUser?.GrevId is null || _gameSettingsEntry is null) return;
        _pendingGameArtworkSlot = slot;
        _gameArtworkCurrentPath = null;
        _gameArtworkPickerView.SetPurpose(
            slot == GameVisualAssetSlot.Icon ? "Choose Console Logo" : "Choose Full Game Tile",
            slot == GameVisualAssetSlot.Icon ? "console logo" : "full game tile");
        ShowGameArtworkHome();
        _navigation.Navigate(Route.GameArtworkPicker);
    }

    private void ShowGameArtworkHome()
    {
        _gameArtworkCurrentPath = null;
        try
        {
            var locations = _fileSystem.GetHomeLocations(_paths.Root)
                .Where(location => location.Name is not "Test Area" and not "Grev Home Data")
                .ToArray();
            _gameArtworkPickerView.ShowHome(locations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _gameArtworkPickerView.ShowError(ex.Message);
        }
    }

    private void NavigateGameArtworkPath(string path)
    {
        try
        {
            var entries = _fileSystem.GetEntries(path);
            _gameArtworkCurrentPath = path;
            _gameArtworkPickerView.ShowDirectory(path, entries, _fileSystem.GetParent(path) is not null);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _gameArtworkPickerView.ShowError(ex.Message);
        }
    }

    private void NavigateGameArtworkUp()
    {
        if (string.IsNullOrWhiteSpace(_gameArtworkCurrentPath))
        {
            ShowGameArtworkHome();
            return;
        }
        var parent = _fileSystem.GetParent(_gameArtworkCurrentPath);
        if (parent is null) ShowGameArtworkHome();
        else NavigateGameArtworkPath(parent);
    }

    private async Task SaveSelectedGameArtworkAsync(string path)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        var slot = _pendingGameArtworkSlot;
        if (service is null || primary?.GrevId is null || _gameSettingsEntry is null) return;

        try
        {
            _gameSettingsEntry = await service.SaveCustomAssetAsync(primary.GrevId, _gameSettingsEntry.GameId, slot, path);
            if (_navigation.Current == Route.GameArtworkPicker) _navigation.GoBack();
            await RefreshProfileGamesAsync();
            _gameSettingsView.ShowStatus(slot == GameVisualAssetSlot.Icon
                ? "Custom console logo saved for this GrevID."
                : "Custom full game tile saved for this GrevID and applied to Installed Apps and Home.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _gameArtworkPickerView.ShowError($"Could not save the artwork: {ex.Message}");
        }
    }

    private async Task UseReusableGameIconAsync(string iconPath)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _gameSettingsEntry is null) return;
        try
        {
            _gameSettingsEntry = await service.UseReusableIconAsync(primary.GrevId, _gameSettingsEntry.GameId, iconPath);
            RenderGameSettings();
            await RefreshProfileGamesAsync();
            _gameSettingsView.ShowStatus("Saved console logo applied at the top-left. The full-tile artwork was left unchanged.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _gameSettingsView.ShowStatus($"Could not apply the saved icon: {ex.Message}");
        }
    }

    private async Task ResetGamePresentationAsync()
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _gameSettingsEntry is null) return;
        try
        {
            _gameSettingsEntry = await service.ResetPresentationAsync(primary.GrevId, _gameSettingsEntry.GameId);
            RenderGameSettings();
            await RefreshProfileGamesAsync();
            _gameSettingsView.ShowStatus("Game name, console logo and full tile restored to their defaults. Saved reusable console logos were kept.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _gameSettingsView.ShowStatus($"Could not restore defaults: {ex.Message}");
        }
    }

    private async Task SaveGamePresentationLayoutAsync(GamePresentationLayout layout)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _gameSettingsEntry is null) return;
        try
        {
            _gameSettingsEntry = await service.SavePresentationLayoutAsync(primary.GrevId, _gameSettingsEntry.GameId, layout);
            RenderGameSettings();
            await RefreshProfileGamesAsync();
            _gameSettingsView.ShowStatus("Tile colour and console-logo layout saved for this GrevID.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _gameSettingsView.ShowStatus($"Could not save the presentation layout: {ex.Message}");
        }
    }

    private void InvalidateProfileGameLibrary()
    {
        Interlocked.Increment(ref _gameLibraryRefreshGeneration);
        _renderedGameLibraryOwnerGrevId = null;
        var primary = _session.PrimaryUser;
        _installedLibraryView.SetGames(Array.Empty<GameLibraryEntry>(), primary);
        _dashboardView.SetGames(Array.Empty<GameLibraryEntry>());

        if (_navigation.Current is Route.InstalledLibrary or Route.Dashboard)
        {
            _ = RefreshProfileGamesAsync();
        }
    }

    private void OpenGameAdd()
    {
        var primary = _session.PrimaryUser;
        if (primary?.GrevId is null)
        {
            _installedLibraryView.ShowGameStatus("A persistent Primary GrevID is required to add individual games.");
            return;
        }

        RenderGameAdd();
        _navigation.Navigate(Route.GameAdd);
    }

    private void RenderGameAdd()
    {
        var primary = _session.PrimaryUser;
        if (primary?.GrevId is null)
        {
            return;
        }
        _gameAddView.SetOwner(primary.DisplayName, primary.GrevId);
    }

    private void OpenGameFilePicker(GamePlatform platform)
    {
        var primary = _session.PrimaryUser;
        if (primary?.GrevId is null)
        {
            _gameAddView.ShowStatus("A persistent Primary GrevID is required to add games.");
            return;
        }

        _pendingGamePlatform = platform;
        _gameFileCurrentPath = null;
        _gameFilePickerView.SetPlatform(platform);
        ShowGameFileHome();
        _navigation.Navigate(Route.GameFilePicker);
    }

    private void ShowGameFileHome()
    {
        _gameFileCurrentPath = null;
        try
        {
            var locations = _fileSystem.GetHomeLocations(_paths.Root)
                .Where(location => location.Name is not "Test Area" and not "Grev Home Data")
                .ToArray();
            _gameFilePickerView.ShowHome(locations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _gameFilePickerView.ShowError(ex.Message);
        }
    }

    private void NavigateGameFilePath(string path)
    {
        try
        {
            var entries = _fileSystem.GetEntries(path);
            _gameFileCurrentPath = path;
            _gameFilePickerView.ShowDirectory(path, entries, _fileSystem.GetParent(path) is not null);
            FocusRouteSoon();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _gameFilePickerView.ShowError(ex.Message);
        }
    }

    private void NavigateGameFileUp()
    {
        if (string.IsNullOrWhiteSpace(_gameFileCurrentPath))
        {
            ShowGameFileHome();
            return;
        }

        var parent = _fileSystem.GetParent(_gameFileCurrentPath);
        if (parent is null) ShowGameFileHome();
        else NavigateGameFilePath(parent);
    }

    private async Task AddSelectedGameAsync(string path)
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null)
        {
            return;
        }
        if (_gameAddInProgress)
        {
            _gameFilePickerView.ShowError("That game is already being added.");
            return;
        }

        _gameAddInProgress = true;
        try
        {
            var game = await service.AddAsync(primary.GrevId, _pendingGamePlatform, path);

            // GameFilePicker -> GameAdd -> InstalledLibrary. Return through both existing history
            // entries so Installed keeps the same Back destination it had before Add Game.
            if (_navigation.Current == Route.GameFilePicker)
            {
                _navigation.GoBack();
            }
            if (_navigation.Current == Route.GameAdd)
            {
                _navigation.GoBack();
            }

            await RefreshProfileGamesAsync();
            _installedLibraryView.ShowGameStatus(
                $"Added {game.DisplayName} • {GameLibraryService.GetPlatformDisplayName(game.Platform)}. It is now available here and on Home.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            _gameFilePickerView.ShowError(ex.Message);
        }
        finally
        {
            _gameAddInProgress = false;
        }
    }

    private async Task RefreshProfileGamesAsync()
    {
        var service = _gameLibraryService;
        var primary = _session.PrimaryUser;
        if (service is null)
        {
            return;
        }

        if (primary?.GrevId is null)
        {
            Interlocked.Increment(ref _gameLibraryRefreshGeneration);
            _renderedGameLibraryOwnerGrevId = null;
            _installedLibraryView.SetGames(Array.Empty<GameLibraryEntry>(), primary);
            _dashboardView.SetGames(Array.Empty<GameLibraryEntry>());
            return;
        }

        var ownerGrevId = primary.GrevId;
        var generation = Interlocked.Increment(ref _gameLibraryRefreshGeneration);
        if (!string.Equals(_renderedGameLibraryOwnerGrevId, ownerGrevId, StringComparison.OrdinalIgnoreCase))
        {
            _installedLibraryView.SetGames(Array.Empty<GameLibraryEntry>(), primary);
            _dashboardView.SetGames(Array.Empty<GameLibraryEntry>());
        }

        try
        {
            var games = await service.GetForProfileAsync(ownerGrevId);
            if (generation != Volatile.Read(ref _gameLibraryRefreshGeneration) ||
                !string.Equals(_session.PrimaryUser?.GrevId, ownerGrevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _renderedGameLibraryOwnerGrevId = ownerGrevId;
            _installedLibraryView.SetGames(games, primary);
            _dashboardView.SetGames(games);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (generation != Volatile.Read(ref _gameLibraryRefreshGeneration) ||
                !string.Equals(_session.PrimaryUser?.GrevId, ownerGrevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _renderedGameLibraryOwnerGrevId = null;
            _installedLibraryView.SetGames(Array.Empty<GameLibraryEntry>(), primary);
            _dashboardView.SetGames(Array.Empty<GameLibraryEntry>());
            _installedLibraryView.ShowGameStatus($"Game library unavailable: {ex.Message}");
            _dashboardView.ShowStatus($"Game library unavailable: {ex.Message}");
        }
    }

    private async Task LaunchGameAsync(GameLibraryEntry game)
    {
        var primary = _session.PrimaryUser;
        if (primary?.GrevId is null)
        {
            ShowGameLaunchError("Choose a persistent Primary User before launching a game.");
            return;
        }

        try
        {
            var installedApps = await _installedApps.GetInstalledForUserAsync(primary.GrevId);
            var runtimeEntry = _gameLaunchResolver.Resolve(game, installedApps, primary.GrevId);
            var launched = await _runtimeSessions.LaunchAsync(runtimeEntry, _session);
            _foregroundLaunchSessionId = launched.LaunchSessionId;
            _installedLibraryView.ShowLaunchStarted(launched);
            _dashboardView.ShowStatus($"Starting {game.DisplayName}…");
            UpdateRuntimeSurfaces();
            Hide();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception)
        {
            ShowGameLaunchError(ex.Message);
        }
    }

    private void ShowGameLaunchError(string message)
    {
        if (_navigation.Current == Route.Dashboard)
        {
            _dashboardView.ShowStatus($"Game launch failed: {message}");
        }
        else
        {
            _installedLibraryView.ShowLaunchError(message);
        }
    }
}
