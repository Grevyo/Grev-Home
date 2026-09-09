using System.IO;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Presentation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly DashboardTileSettingsView _dashboardTileSettingsView = new();
    private readonly ProfilePhotoPickerView _dashboardTileArtworkPicker = new();
    private DashboardTilePresentationService? _dashboardTilePresentation;
    private string? _dashboardTileSettingsId;
    private string? _dashboardArtworkPath;
    private bool _dashboardPresentationReady;

    private void InitializeDashboardPresentationIntegration()
    {
        if (_dashboardPresentationReady) return;
        _dashboardPresentationReady = true;
        _dashboardTilePresentation = new DashboardTilePresentationService(_paths);

        _dashboardView.TileSettingsRequested += OpenDashboardTileSettings;
        _dashboardTileSettingsView.BackRequested += (_, _) => _navigation.GoBack();
        _dashboardTileSettingsView.SaveRequested += (name, color) => _ = SaveDashboardTileAsync(name, color);
        _dashboardTileSettingsView.ChooseMediaRequested += (_, _) => OpenDashboardArtworkPicker();
        _dashboardTileSettingsView.ReusableMediaRequested += path => _ = SaveDashboardTileMediaAsync(path);
        _dashboardTileSettingsView.ResetRequested += (_, _) => _ = ResetDashboardTileAsync();
        _dashboardTileArtworkPicker.HomeRequested += (_, _) => ShowDashboardArtworkHome();
        _dashboardTileArtworkPicker.UpRequested += (_, _) => NavigateDashboardArtworkUp();
        _dashboardTileArtworkPicker.CancelRequested += (_, _) => _navigation.GoBack();
        _dashboardTileArtworkPicker.NavigateRequested += NavigateDashboardArtwork;
        _dashboardTileArtworkPicker.PhotoSelected += path => _ = SaveDashboardTileMediaAsync(path);

        _controllerInput.ActionPressed += input =>
        {
            if (input.Action == InputAction.Accept && _navigation.Current == Route.Dashboard)
                Dispatcher.Invoke(() => _dashboardView.BeginControllerTilePress());
        };
        _controllerInput.AcceptLongPressed += _ => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_navigation.Current == Route.Dashboard) _dashboardView.HandleControllerTileLongPress();
        }));
        _controllerInput.AcceptReleased += _ => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_navigation.Current == Route.Dashboard) _dashboardView.CompleteControllerTilePress();
        }));
        _controllerInput.AcceptCancelled += _ => Dispatcher.BeginInvoke(new Action(() =>
        {
            _dashboardView.CancelControllerTilePress();
        }));

        _navigation.RouteChanged += route =>
        {
            if (route == Route.Dashboard) _ = RefreshDashboardTilesAsync();
            else if (route == Route.DashboardTileSettings) { RouteHost.Content = _dashboardTileSettingsView; _ = RenderDashboardTileSettingsAsync(); }
            else if (route == Route.DashboardTileArtworkPicker) RouteHost.Content = _dashboardTileArtworkPicker;
        };
        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = RefreshDashboardTilesAsync()));
        _ = RefreshDashboardTilesAsync();
    }

    private void OpenDashboardTileSettings(string tileId)
    {
        if (_session.PrimaryUser?.GrevId is null) { _dashboardView.ShowStatus("A permanent Primary User is required to customise Home buttons."); return; }
        _dashboardTileSettingsId = tileId;
        _navigation.Navigate(Route.DashboardTileSettings);
    }

    private async Task RenderDashboardTileSettingsAsync()
    {
        var service = _dashboardTilePresentation; var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null || _dashboardTileSettingsId is null) return;
        var tile = await service.ResolveAsync(primary.GrevId, _dashboardTileSettingsId);
        _dashboardTileSettingsView.SetTile(tile, $"{primary.DisplayName} • {primary.GrevId}", service.GetReusableMedia(primary.GrevId));
    }

    private async Task RefreshDashboardTilesAsync()
    {
        var service = _dashboardTilePresentation; var grevId = _session.PrimaryUser?.GrevId;
        if (service is null || grevId is null)
        {
            var defaults = new Dictionary<string, ResolvedDashboardTile>();
            _dashboardView.SetTilePresentations(defaults);
            _settingsView.SetTilePresentations(defaults);
            return;
        }
        try
        {
            var presentations = await service.ResolveAllAsync(grevId);
            _dashboardView.SetTilePresentations(presentations);
            _settingsView.SetTilePresentations(presentations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { _dashboardView.ShowStatus($"Home button appearance could not be loaded: {ex.Message}"); }
    }

    private async Task SaveDashboardTileAsync(string name, string color)
    {
        var service = _dashboardTilePresentation; var grevId = _session.PrimaryUser?.GrevId;
        if (service is null || grevId is null || _dashboardTileSettingsId is null) return;
        try { await service.SaveAsync(grevId, _dashboardTileSettingsId, name, color); await RefreshDashboardTilesAsync(); await RenderDashboardTileSettingsAsync(); _dashboardTileSettingsView.ShowStatus("Home button saved for this GrevID."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { _dashboardTileSettingsView.ShowStatus($"Could not save Home button: {ex.Message}"); }
    }

    private void OpenDashboardArtworkPicker()
    {
        _dashboardArtworkPath = null;
        _dashboardTileArtworkPicker.SetPurpose("Choose Full Home Button", "Home button");
        ShowDashboardArtworkHome();
        _navigation.Navigate(Route.DashboardTileArtworkPicker);
    }

    private void ShowDashboardArtworkHome()
    {
        _dashboardArtworkPath = null;
        _dashboardTileArtworkPicker.ShowHome(_fileSystem.GetHomeLocations(_paths.Root).Where(location => location.Name is not "Test Area" and not "Grev Home Data").ToArray());
    }

    private void NavigateDashboardArtwork(string path)
    {
        try { _dashboardArtworkPath = Path.GetFullPath(path); _dashboardTileArtworkPicker.ShowDirectory(_dashboardArtworkPath, _fileSystem.GetEntries(_dashboardArtworkPath), Directory.GetParent(_dashboardArtworkPath) is not null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _dashboardTileArtworkPicker.ShowError(ex.Message); }
    }

    private void NavigateDashboardArtworkUp()
    {
        if (_dashboardArtworkPath is null) return;
        var parent = Directory.GetParent(_dashboardArtworkPath);
        if (parent is null) ShowDashboardArtworkHome(); else NavigateDashboardArtwork(parent.FullName);
    }

    private async Task SaveDashboardTileMediaAsync(string path)
    {
        var service = _dashboardTilePresentation; var grevId = _session.PrimaryUser?.GrevId;
        if (service is null || grevId is null || _dashboardTileSettingsId is null) return;
        try { await service.SaveMediaAsync(grevId, _dashboardTileSettingsId, path); await RefreshDashboardTilesAsync(); if (_navigation.Current == Route.DashboardTileArtworkPicker) _navigation.GoBack(); await RenderDashboardTileSettingsAsync(); _dashboardTileSettingsView.ShowStatus("Full Home button artwork saved."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { _dashboardTileSettingsView.ShowStatus($"Could not save Home artwork: {ex.Message}"); }
    }

    private async Task ResetDashboardTileAsync()
    {
        var service = _dashboardTilePresentation; var grevId = _session.PrimaryUser?.GrevId;
        if (service is null || grevId is null || _dashboardTileSettingsId is null) return;
        await service.ResetAsync(grevId, _dashboardTileSettingsId); await RefreshDashboardTilesAsync(); await RenderDashboardTileSettingsAsync(); _dashboardTileSettingsView.ShowStatus("Home button reset to its active theme/default appearance.");
    }
}
