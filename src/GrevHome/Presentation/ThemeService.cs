using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Presentation;

/// <summary>
/// Which theme is active plus every custom theme saved on this machine. Themes are machine-wide,
/// not per-GrevID: one signed-in shell has one chrome palette, matching how the shell itself is a
/// single persistent window rather than a per-account skin.
/// </summary>
public sealed record ThemeState(string ActiveThemeId, IReadOnlyList<ThemeDefinition> CustomThemes)
{
    public static ThemeState Default { get; } = new(ThemeCatalog.DefaultThemeId, []);
}

/// <summary>
/// Loads, saves and resolves Grev Home themes. Built-in themes live in code (<see cref="ThemeCatalog"/>)
/// and can never be edited or deleted; custom themes are saved as individual JSON files under the
/// machine's reserved <c>Themes</c> folder, one file per theme, so a theme can be copied to another
/// Grev Home machine by copying its file. The active theme id is a small machine-wide setting
/// stored alongside shell motion settings.
/// </summary>
public sealed class ThemeService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = JsonDefaults.Indented;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ThemeService(AppPaths paths) => _paths = paths;

    private string ActiveThemeFile => Path.Combine(_paths.PresentationData, "active-theme.json");

    public async Task<ThemeState> LoadAsync(CancellationToken cancellationToken = default)
    {
        var activeId = await LoadActiveIdAsync(cancellationToken);
        var custom = await LoadCustomThemesAsync(cancellationToken);
        return new ThemeState(activeId, custom);
    }

    /// <summary>
    /// Every theme available for selection: built-ins first, then custom themes in save order.
    /// </summary>
    public IReadOnlyList<ThemeDefinition> ResolveAll(ThemeState state) =>
        [.. ThemeCatalog.BuiltIn, .. state.CustomThemes];

    public ThemeDefinition ResolveActive(ThemeState state) =>
        ResolveAll(state).FirstOrDefault(theme => string.Equals(theme.Id, state.ActiveThemeId, StringComparison.OrdinalIgnoreCase))
        ?? ThemeCatalog.Default;

    public async Task SetActiveThemeAsync(string themeId, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.PresentationData);
            await WriteJsonAsync(ActiveThemeFile, new ActiveThemeRecord(themeId), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Saves a custom theme, creating it on a new Id or overwriting an existing custom theme with
    /// the same Id. Built-in ids are reserved: a save can never shadow or replace a shipped theme.
    /// </summary>
    public async Task<ThemeDefinition> SaveCustomThemeAsync(ThemeDefinition theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (ThemeCatalog.FindBuiltIn(theme.Id) is not null)
            throw new InvalidOperationException("Built-in themes cannot be overwritten. Save this as a new theme instead.");

        var saved = theme with { IsBuiltIn = false };
        saved.Validate();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.Themes);
            await WriteJsonAsync(GetCustomThemeFile(saved.Id), saved, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }

        return saved;
    }

    /// <summary>
    /// Deletes a custom theme. Deleting the active theme is allowed - the caller falls back to
    /// the shipped default the same way a missing/corrupt active-theme id already does.
    /// </summary>
    public async Task DeleteCustomThemeAsync(string themeId, CancellationToken cancellationToken = default)
    {
        if (ThemeCatalog.FindBuiltIn(themeId) is not null)
            throw new InvalidOperationException("Built-in themes cannot be deleted.");

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var path = GetCustomThemeFile(themeId);
            if (File.Exists(path)) await Task.Run(() => File.Delete(path), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<string> LoadActiveIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(ActiveThemeFile)) return ThemeCatalog.DefaultThemeId;
            await using var stream = File.OpenRead(ActiveThemeFile);
            var record = await JsonSerializer.DeserializeAsync<ActiveThemeRecord>(stream, _json, cancellationToken);
            return string.IsNullOrWhiteSpace(record?.ThemeId) ? ThemeCatalog.DefaultThemeId : record.ThemeId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ThemeCatalog.DefaultThemeId;
        }
    }

    private async Task<IReadOnlyList<ThemeDefinition>> LoadCustomThemesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.Themes)) return [];
        var themes = new List<ThemeDefinition>();
        foreach (var file in Directory.EnumerateFiles(_paths.Themes, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var theme = await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, _json, cancellationToken);
                // A theme file that no longer parses or validates is skipped rather than crashing
                // the whole theme list; it stays on disk untouched in case it can be repaired.
                if (theme is null) continue;
                theme.Validate();
                if (ThemeCatalog.FindBuiltIn(theme.Id) is not null) continue;
                themes.Add(theme with { IsBuiltIn = false });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // Skip this one file; every other saved theme still loads.
            }
        }
        return themes;
    }

    private string GetCustomThemeFile(string themeId) =>
        Path.Combine(_paths.Themes, $"{SanitizeFileName(themeId)}.json");

    private static string SanitizeFileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record ActiveThemeRecord(string ThemeId);
}
