using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Presentation;

/// <summary>
/// Which theme is active plus the custom themes available in one scope. The machine scope is the
/// Admin-owned default. A GrevID scope may leave ActiveThemeId null to inherit that default.
/// </summary>
public sealed record ThemeState(string? ActiveThemeId, IReadOnlyList<ThemeDefinition> CustomThemes)
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

    private string ActiveThemeFile(string? grevId) => grevId is null
        ? Path.Combine(_paths.PresentationData, "active-theme.json")
        : Path.Combine(_paths.GetProfileThemes(grevId), "active-theme.json");

    private string ThemeDirectory(string? grevId) => grevId is null ? _paths.Themes : _paths.GetProfileThemes(grevId);

    public async Task<ThemeState> LoadAsync(CancellationToken cancellationToken = default)
    {
        var activeId = await LoadActiveIdAsync(null, ThemeCatalog.DefaultThemeId, cancellationToken);
        var custom = await LoadCustomThemesAsync(null, cancellationToken);
        return new ThemeState(activeId, custom);
    }

    public async Task<ThemeState> LoadForProfileAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var activeId = await LoadActiveIdAsync(grevId, null, cancellationToken);
        var custom = await LoadCustomThemesAsync(grevId, cancellationToken);
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

    public ThemeDefinition ResolveForProfile(ThemeState profileState, ThemeState machineState) =>
        profileState.ActiveThemeId is null
            ? ResolveActive(machineState)
            : ResolveActive(profileState);

    public async Task SetActiveThemeAsync(string themeId, string? grevId = null, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var directory = grevId is null ? _paths.PresentationData : _paths.GetProfileThemes(grevId);
            Directory.CreateDirectory(directory);
            await WriteJsonAsync(ActiveThemeFile(grevId), new ActiveThemeRecord(themeId), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task ClearProfileOverrideAsync(string grevId, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var path = ActiveThemeFile(grevId);
            if (File.Exists(path)) File.Delete(path);
        }
        finally { _writeGate.Release(); }
    }

    /// <summary>
    /// Saves a custom theme, creating it on a new Id or overwriting an existing custom theme with
    /// the same Id. Built-in ids are reserved: a save can never shadow or replace a shipped theme.
    /// </summary>
    public async Task<ThemeDefinition> SaveCustomThemeAsync(ThemeDefinition theme, string? grevId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (ThemeCatalog.FindBuiltIn(theme.Id) is not null)
            throw new InvalidOperationException("Built-in themes cannot be overwritten. Save this as a new theme instead.");

        var saved = theme with { IsBuiltIn = false };
        saved.Validate();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(ThemeDirectory(grevId));
            await WriteJsonAsync(GetCustomThemeFile(saved.Id, grevId), saved, cancellationToken);
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
    public async Task DeleteCustomThemeAsync(string themeId, string? grevId = null, CancellationToken cancellationToken = default)
    {
        if (ThemeCatalog.FindBuiltIn(themeId) is not null)
            throw new InvalidOperationException("Built-in themes cannot be deleted.");

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var path = GetCustomThemeFile(themeId, grevId);
            if (File.Exists(path)) await Task.Run(() => File.Delete(path), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Writes a theme to a standalone JSON file for sharing - copy it to another Grev Home
    /// machine's Themes folder, or hand it to <see cref="ImportThemeAsync"/> there. Exports the
    /// exact colors currently being edited, saved or not: exporting is read-only and never
    /// requires the theme to exist as a saved custom theme first.
    /// </summary>
    public async Task<string> ExportThemeAsync(ThemeDefinition theme, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);
        theme.Validate();
        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, $"{SanitizeFileName(theme.Name)}.theme.json");
        // Exported files never collide with each other or overwrite a previous export of the same
        // theme silently: each export gets its own timestamped name.
        if (File.Exists(path))
            path = Path.Combine(targetDirectory, $"{SanitizeFileName(theme.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.theme.json");
        await WriteJsonAsync(path, theme with { IsBuiltIn = false }, cancellationToken);
        return path;
    }

    /// <summary>
    /// Reads and validates a theme file exported by <see cref="ExportThemeAsync"/> (or any hand-
    /// written file in the same shape). Never writes anything and never touches the active theme
    /// or the saved custom theme list - the caller decides whether to save it, exactly like a
    /// freshly created "New Theme" draft. A colliding Id with an existing saved theme is resolved
    /// by the caller when it saves, not silently here.
    /// </summary>
    public async Task<ThemeDefinition> ImportThemeAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("That theme file no longer exists.", sourcePath);
        ThemeDefinition? theme;
        try
        {
            await using var stream = File.OpenRead(sourcePath);
            theme = await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, _json, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("That file is not a valid Grev Home theme.", ex);
        }

        if (theme is null) throw new InvalidOperationException("That file is not a valid Grev Home theme.");
        theme = theme with { IsBuiltIn = false };
        theme.Validate();
        return theme;
    }

    private async Task<string?> LoadActiveIdAsync(string? grevId, string? fallback, CancellationToken cancellationToken)
    {
        try
        {
            var path = ActiveThemeFile(grevId);
            if (!File.Exists(path)) return fallback;
            await using var stream = File.OpenRead(path);
            var record = await JsonSerializer.DeserializeAsync<ActiveThemeRecord>(stream, _json, cancellationToken);
            return string.IsNullOrWhiteSpace(record?.ThemeId) ? fallback : record.ThemeId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    private async Task<IReadOnlyList<ThemeDefinition>> LoadCustomThemesAsync(string? grevId, CancellationToken cancellationToken)
    {
        var directory = ThemeDirectory(grevId);
        if (!Directory.Exists(directory)) return [];
        var themes = new List<ThemeDefinition>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json")
                     .Where(path => !string.Equals(Path.GetFileName(path), "active-theme.json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
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

    private string GetCustomThemeFile(string themeId, string? grevId) =>
        Path.Combine(ThemeDirectory(grevId), $"{SanitizeFileName(themeId)}.json");

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
