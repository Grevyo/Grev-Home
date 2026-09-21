using System.IO;
using GrevHome.Navigation;
using GrevHome.Presentation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ThemeCreatorView _themeCreatorView = new();
    private readonly ThemeFilePickerView _themeFilePickerView = new();
    private ThemeService? _themeService;
    private ThemeState _machineThemeState = ThemeState.Default;
    private ThemeState _profileThemeState = new(null, []);
    private bool _themeMachineScope;
    private ThemeDefinition _themeEditingDraft = ThemeCatalog.Default;
    private bool _themeEditingDraftIsSaved;
    private bool _themeCreatorReady;
    private string? _themeImportPath;

    private void InitializeThemeCreatorIntegration()
    {
        if (_themeCreatorReady) return;
        _themeCreatorReady = true;
        _themeService = new ThemeService(_paths);

        _themeCreatorView.BackRequested += (_, _) => CloseThemeCreator();
        _themeCreatorView.ActivateRequested += themeId => _ = ActivateThemeAsync(themeId);
        _themeCreatorView.NewThemeRequested += (_, _) => BeginNewThemeDraft();
        _themeCreatorView.SaveRequested += (theme, asNew) => _ = SaveThemeAsync(theme, asNew);
        _themeCreatorView.DeleteRequested += themeId => _ = DeleteThemeAsync(themeId);
        _themeCreatorView.ExportRequested += theme => _ = ExportThemeAsync(theme);
        _themeCreatorView.ImportRequested += (_, _) => OpenThemeFilePicker();
        _themeCreatorView.SwitchScopeRequested += (_, _) => _ = SwitchThemeScopeAsync();
        _themeCreatorView.UseMachineDefaultRequested += (_, _) => _ = UseMachineDefaultAsync();
        _settingsView.ManageThemesRequested += (_, _) => OpenThemeCreator();
        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_themeMachineScope && !CanUseAdminConsole()) _themeMachineScope = false;
            await ApplyEffectiveThemeAsync();
            if (_navigation.Current == Route.ThemeCreator) await OpenThemeCreatorAsync();
        }));

        _themeFilePickerView.HomeRequested += (_, _) => ShowThemeFilePickerHome();
        _themeFilePickerView.UpRequested += (_, _) => NavigateThemeFilePickerUp();
        _themeFilePickerView.CancelRequested += (_, _) => _navigation.GoBack();
        _themeFilePickerView.NavigateRequested += NavigateThemeFilePicker;
        _themeFilePickerView.FileSelected += path => _ = ImportThemeAsync(path);

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ThemeCreator) { RouteHost.Content = _themeCreatorView; _ = OpenThemeCreatorAsync(); }
            else if (route == Route.ThemeFilePicker) RouteHost.Content = _themeFilePickerView;
        };
    }

    private void OpenThemeCreator()
    {
        _navigation.Navigate(Route.ThemeCreator);
    }

    private void CloseThemeCreator()
    {
        // Editing previews live, so anything not explicitly saved is undone here: whatever was
        // actually active before the Theme Creator opened is re-applied on the way out, exactly
        // like closing a document without saving.
        _ = ApplyEffectiveThemeAsync();
        _navigation.GoBack();
    }

    private async Task OpenThemeCreatorAsync()
    {
        var service = _themeService;
        if (service is null) return;
        try
        {
            _machineThemeState = await service.LoadAsync();
            var grevId = _session.PrimaryUser?.GrevId;
            _profileThemeState = grevId is null
                ? new ThemeState(null, [])
                : await service.LoadForProfileAsync(grevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _themeCreatorView.ShowStatus($"Could not load saved themes: {ex.Message}");
        }

        _themeMachineScope = false;
        var active = ResolveEditingActive(service);
        _themeEditingDraft = active;
        _themeEditingDraftIsSaved = !active.IsBuiltIn;
        RenderThemeCreator();
    }

    private void BeginNewThemeDraft()
    {
        // A fresh, unsaved theme starting from whatever is currently being edited. It has a real
        // id from the moment it exists so its color fields work like any other draft, but that id
        // never collides with a saved theme until Save actually persists it.
        _themeEditingDraft = _themeEditingDraft with { Id = $"draft-{Guid.NewGuid():N}", Name = "New Theme", IsBuiltIn = false };
        _themeEditingDraftIsSaved = false;
        RenderThemeCreator();
    }

    private async Task ActivateThemeAsync(string themeId)
    {
        var service = _themeService;
        if (service is null) return;
        if (!CanEditCurrentThemeScope())
        {
            _themeCreatorView.ShowStatus("This theme scope is no longer available to the current Primary User.");
            return;
        }
        var state = CurrentThemeState;
        var theme = service.ResolveAll(state).FirstOrDefault(candidate => string.Equals(candidate.Id, themeId, StringComparison.OrdinalIgnoreCase));
        if (theme is null) return;

        try
        {
            var grevId = CurrentThemeOwnerGrevId;
            await service.SetActiveThemeAsync(theme.Id, grevId);
            if (_themeMachineScope) _machineThemeState = _machineThemeState with { ActiveThemeId = theme.Id };
            else _profileThemeState = _profileThemeState with { ActiveThemeId = theme.Id };
            ThemeApplier.Apply(theme);
            _themeEditingDraft = theme;
            _themeEditingDraftIsSaved = !theme.IsBuiltIn;
            RenderThemeCreator();
            _themeCreatorView.ShowStatus($"{theme.Name} is now the active theme.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _themeCreatorView.ShowStatus($"Could not set the active theme: {ex.Message}");
        }
    }

    private async Task SaveThemeAsync(ThemeDefinition draft, bool asNew)
    {
        var service = _themeService;
        if (service is null) return;
        if (!CanEditCurrentThemeScope())
        {
            _themeCreatorView.ShowStatus("This theme scope is no longer available to the current Primary User.");
            return;
        }

        var target = asNew || !_themeEditingDraftIsSaved || draft.IsBuiltIn
            ? draft with { Id = GenerateThemeId(draft.Name), IsBuiltIn = false }
            : draft;

        try
        {
            target.Validate();
            var grevId = CurrentThemeOwnerGrevId;
            var saved = await service.SaveCustomThemeAsync(target, grevId);
            await service.SetActiveThemeAsync(saved.Id, grevId);
            await ReloadThemeStatesAsync(service);
            ThemeApplier.Apply(saved);
            _themeEditingDraft = saved;
            _themeEditingDraftIsSaved = true;
            RenderThemeCreator();
            // A contrast warning never blocks the save - a deliberately low-contrast look is a
            // legitimate choice - but it must not be silently swallowed by the ordinary success
            // status either, so it replaces that status instead of sitting alongside it.
            var warnings = saved.GetContrastWarnings();
            _themeCreatorView.ShowStatus(warnings.Count == 0
                ? $"{saved.Name} saved and set as the active theme."
                : $"{saved.Name} saved and set as the active theme. {warnings[0]}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _themeCreatorView.ShowStatus($"Could not save this theme: {ex.Message}");
        }
    }

    private async Task DeleteThemeAsync(string themeId)
    {
        var service = _themeService;
        if (service is null || ThemeCatalog.FindBuiltIn(themeId) is not null) return;
        if (!CanEditCurrentThemeScope())
        {
            _themeCreatorView.ShowStatus("This theme scope is no longer available to the current Primary User.");
            return;
        }

        try
        {
            await service.DeleteCustomThemeAsync(themeId, CurrentThemeOwnerGrevId);
            if (string.Equals(CurrentThemeState.ActiveThemeId, themeId, StringComparison.OrdinalIgnoreCase))
            {
                if (_themeMachineScope)
                    await service.SetActiveThemeAsync(ThemeCatalog.DefaultThemeId);
                else if (CurrentThemeOwnerGrevId is { } grevId)
                    await service.ClearProfileOverrideAsync(grevId);
            }
            await ReloadThemeStatesAsync(service);
            var active = ResolveEditingActive(service);
            ThemeApplier.Apply(active);
            _themeEditingDraft = active;
            _themeEditingDraftIsSaved = !active.IsBuiltIn;
            RenderThemeCreator();
            _themeCreatorView.ShowStatus("Theme deleted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _themeCreatorView.ShowStatus($"Could not delete this theme: {ex.Message}");
        }
    }

    private void RenderThemeCreator()
    {
        var service = _themeService;
        if (service is null) return;
        _themeCreatorView.SetEditing(_themeEditingDraft);
        var state = CurrentThemeState;
        _themeCreatorView.SetGallery(service.ResolveAll(state), state.ActiveThemeId ?? string.Empty, _themeEditingDraft.Id);
        var canManageMachine = CanUseAdminConsole();
        _themeCreatorView.SetScope(_themeMachineScope, canManageMachine, !_themeMachineScope && state.ActiveThemeId is null);
        var canEditScope = _themeMachineScope ? canManageMachine : CurrentThemeOwnerGrevId is not null;
        _themeCreatorView.SetSaveAvailability(
            canEdit: canEditScope,
            canOverwrite: canEditScope && !_themeEditingDraft.IsBuiltIn && _themeEditingDraftIsSaved,
            canDelete: canEditScope && !_themeEditingDraft.IsBuiltIn && _themeEditingDraftIsSaved);
        // SetEditing above already applies the draft live; nothing further to do here.
    }

    private async Task ExportThemeAsync(ThemeDefinition theme)
    {
        var service = _themeService;
        if (service is null) return;
        try
        {
            var path = await service.ExportThemeAsync(theme, _paths.Downloads);
            _themeCreatorView.ShowStatus($"Exported to {path}. Copy this file to share the theme or import it on another Grev Home machine.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _themeCreatorView.ShowStatus($"Could not export this theme: {ex.Message}");
        }
    }

    private void OpenThemeFilePicker()
    {
        _themeImportPath = null;
        ShowThemeFilePickerHome();
        _navigation.Navigate(Route.ThemeFilePicker);
    }

    private void ShowThemeFilePickerHome()
    {
        _themeImportPath = null;
        _themeFilePickerView.ShowHome(_fileSystem.GetHomeLocations(_paths.Root).Where(location => location.Name is not "Test Area" and not "Grev Home Data").ToArray());
    }

    private void NavigateThemeFilePicker(string path)
    {
        try
        {
            _themeImportPath = Path.GetFullPath(path);
            _themeFilePickerView.ShowDirectory(_themeImportPath, _fileSystem.GetEntries(_themeImportPath), Directory.GetParent(_themeImportPath) is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _themeFilePickerView.ShowError(ex.Message);
        }
    }

    private void NavigateThemeFilePickerUp()
    {
        if (_themeImportPath is null) return;
        var parent = Directory.GetParent(_themeImportPath);
        if (parent is null) ShowThemeFilePickerHome(); else NavigateThemeFilePicker(parent.FullName);
    }

    private async Task ImportThemeAsync(string path)
    {
        var service = _themeService;
        if (service is null) return;
        try
        {
            var imported = await service.ImportThemeAsync(path);
            // A colliding Id with a theme already saved on this machine is left to Save/Save as
            // New to resolve, the same as any other draft - importing never overwrites anything
            // by itself.
            _themeEditingDraft = imported;
            _themeEditingDraftIsSaved = false;
            if (_navigation.Current == Route.ThemeFilePicker) _navigation.GoBack();
            RenderThemeCreator();
            _themeCreatorView.ShowStatus($"Imported \"{imported.Name}\". Save it to keep it, or keep editing first.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _themeFilePickerView.ShowError($"Could not import that file: {ex.Message}");
        }
    }

    private static string GenerateThemeId(string name)
    {
        var raw = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var slug = string.Join('-', raw.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(slug)) slug = "theme";
        return $"custom-{slug}-{Guid.NewGuid():N}";
    }

    private ThemeState CurrentThemeState => _themeMachineScope ? _machineThemeState : _profileThemeState;
    private string? CurrentThemeOwnerGrevId => _themeMachineScope ? null : _session.PrimaryUser?.GrevId;
    private bool CanEditCurrentThemeScope() => _themeMachineScope ? CanUseAdminConsole() : CurrentThemeOwnerGrevId is not null;

    private ThemeDefinition ResolveEditingActive(ThemeService service) => _themeMachineScope
        ? service.ResolveActive(_machineThemeState)
        : service.ResolveForProfile(_profileThemeState, _machineThemeState);

    private async Task ReloadThemeStatesAsync(ThemeService service)
    {
        _machineThemeState = await service.LoadAsync();
        var grevId = _session.PrimaryUser?.GrevId;
        _profileThemeState = grevId is null ? new ThemeState(null, []) : await service.LoadForProfileAsync(grevId);
    }

    private async Task ApplyEffectiveThemeAsync()
    {
        var service = _themeService ?? new ThemeService(_paths);
        try
        {
            await ReloadThemeStatesAsync(service);
            ThemeApplier.Apply(service.ResolveForProfile(_profileThemeState, _machineThemeState));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemeApplier.Apply(ThemeCatalog.Default);
        }
    }

    private async Task SwitchThemeScopeAsync()
    {
        if (!CanUseAdminConsole()) return;
        _themeMachineScope = !_themeMachineScope;
        var service = _themeService;
        if (service is null) return;
        var active = ResolveEditingActive(service);
        _themeEditingDraft = active;
        _themeEditingDraftIsSaved = !active.IsBuiltIn;
        RenderThemeCreator();
        _themeCreatorView.ShowStatus(_themeMachineScope
            ? "Editing the Admin-owned machine default."
            : "Editing this Admin GrevID's private theme.");
    }

    private async Task UseMachineDefaultAsync()
    {
        var grevId = _session.PrimaryUser?.GrevId;
        var service = _themeService;
        if (grevId is null || service is null) return;
        await service.ClearProfileOverrideAsync(grevId);
        await ReloadThemeStatesAsync(service);
        var active = service.ResolveActive(_machineThemeState);
        ThemeApplier.Apply(active);
        _themeEditingDraft = active;
        _themeEditingDraftIsSaved = false;
        RenderThemeCreator();
        _themeCreatorView.ShowStatus("This GrevID now follows the Admin's machine default theme.");
    }
}
