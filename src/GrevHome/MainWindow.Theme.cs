using System.IO;
using GrevHome.Navigation;
using GrevHome.Presentation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly ThemeCreatorView _themeCreatorView = new();
    private ThemeService? _themeService;
    private ThemeState _themeState = ThemeState.Default;
    private ThemeDefinition _themeEditingDraft = ThemeCatalog.Default;
    private bool _themeEditingDraftIsSaved;
    private bool _themeCreatorReady;

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
        _settingsView.ManageThemesRequested += (_, _) => OpenThemeCreator();

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ThemeCreator) { RouteHost.Content = _themeCreatorView; _ = OpenThemeCreatorAsync(); }
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
        ThemeApplier.Apply(_themeService?.ResolveActive(_themeState) ?? ThemeCatalog.Default);
        _navigation.GoBack();
    }

    private async Task OpenThemeCreatorAsync()
    {
        var service = _themeService;
        if (service is null) return;
        try
        {
            _themeState = await service.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _themeCreatorView.ShowStatus($"Could not load saved themes: {ex.Message}");
        }

        var active = service.ResolveActive(_themeState);
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
        var theme = service.ResolveAll(_themeState).FirstOrDefault(candidate => string.Equals(candidate.Id, themeId, StringComparison.OrdinalIgnoreCase));
        if (theme is null) return;

        try
        {
            await service.SetActiveThemeAsync(theme.Id);
            _themeState = _themeState with { ActiveThemeId = theme.Id };
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

        var target = asNew || !_themeEditingDraftIsSaved || draft.IsBuiltIn
            ? draft with { Id = GenerateThemeId(draft.Name), IsBuiltIn = false }
            : draft;

        try
        {
            target.Validate();
            var saved = await service.SaveCustomThemeAsync(target);
            await service.SetActiveThemeAsync(saved.Id);
            _themeState = await service.LoadAsync();
            ThemeApplier.Apply(saved);
            _themeEditingDraft = saved;
            _themeEditingDraftIsSaved = true;
            RenderThemeCreator();
            _themeCreatorView.ShowStatus($"{saved.Name} saved and set as the active theme.");
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

        try
        {
            await service.DeleteCustomThemeAsync(themeId);
            _themeState = await service.LoadAsync();
            var active = service.ResolveActive(_themeState);
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
        _themeCreatorView.SetGallery(service.ResolveAll(_themeState), _themeState.ActiveThemeId, _themeEditingDraft.Id);
        _themeCreatorView.SetSaveAvailability(
            canOverwrite: !_themeEditingDraft.IsBuiltIn && _themeEditingDraftIsSaved,
            canDelete: !_themeEditingDraft.IsBuiltIn && _themeEditingDraftIsSaved);
        // SetEditing above already applies the draft live; nothing further to do here.
    }

    private static string GenerateThemeId(string name)
    {
        var raw = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var slug = string.Join('-', raw.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(slug)) slug = "theme";
        return $"custom-{slug}-{Guid.NewGuid():N}";
    }
}
