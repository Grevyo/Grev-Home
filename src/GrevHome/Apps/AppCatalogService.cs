using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Apps;

public sealed class AppCatalogService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public AppCatalogService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<AppDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureMachineLayout();
        if (!File.Exists(_paths.AppCatalogueFile))
        {
            return Array.Empty<AppDefinition>();
        }

        try
        {
            await using var stream = File.OpenRead(_paths.AppCatalogueFile);
            var definitions = await JsonSerializer.DeserializeAsync<List<AppDefinition>>(
                stream,
                _jsonOptions,
                cancellationToken) ?? new List<AppDefinition>();

            return definitions
                .Where(IsValidDefinition)
                .GroupBy(definition => definition.AppId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<AppDefinition>();
        }
    }

    public async Task UpsertAsync(AppDefinition definition, CancellationToken cancellationToken = default)
    {
        ValidateDefinition(definition);
        var definitions = (await GetAllAsync(cancellationToken)).ToList();
        definitions.RemoveAll(existing => string.Equals(existing.AppId, definition.AppId, StringComparison.OrdinalIgnoreCase));
        definitions.Add(definition);
        definitions.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        await WriteAsync(definitions, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<AppDefinition> definitions, CancellationToken cancellationToken)
    {
        _paths.EnsureMachineLayout();
        var temporaryPath = _paths.AppCatalogueFile + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, definitions, _jsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _paths.AppCatalogueFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValidDefinition(AppDefinition definition)
    {
        try
        {
            ValidateDefinition(definition);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateDefinition(AppDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AppIdentity.ValidateAppId(definition.AppId);

        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 100)
        {
            throw new ArgumentException("App name must be between 1 and 100 characters.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Launch.Executable))
        {
            throw new ArgumentException("An app definition must include an executable launch target.", nameof(definition));
        }
    }
}
