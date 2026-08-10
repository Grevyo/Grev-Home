using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Apps;

public sealed record InstalledAppEntry(
    InstalledAppManifest Manifest,
    string BinaryRoot,
    string? DataRoot,
    bool AvailableToCurrentUser,
    string? AvailabilityMessage);

public sealed class InstalledAppService
{
    private const string InstalledManifestName = "installed.grevapp.json";

    private readonly AppPaths _paths;
    private readonly AppPathResolver _pathResolver;
    private readonly AppCatalogService _catalogue;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public InstalledAppService(AppPaths paths, AppPathResolver pathResolver, AppCatalogService catalogue)
    {
        _paths = paths;
        _pathResolver = pathResolver;
        _catalogue = catalogue;
    }

    public async Task<IReadOnlyList<InstalledAppEntry>> GetInstalledForUserAsync(
        string? grevId,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureMachineLayout();
        var manifests = new List<InstalledAppManifest>();

        manifests.AddRange(await ReadManifestsUnderAsync(_paths.GlobalApps, expectedOwnerGrevId: null, cancellationToken));

        if (!string.IsNullOrWhiteSpace(grevId))
        {
            _paths.EnsureProfileLayout(grevId);
            manifests.AddRange(await ReadManifestsUnderAsync(_paths.GetProfileApps(grevId), grevId, cancellationToken));
        }

        var results = new List<InstalledAppEntry>();
        foreach (var manifest in manifests
                     .GroupBy(item => $"{item.Definition.AppId}|{item.OwnerGrevId}", StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.Last()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var resolved = _pathResolver.Resolve(manifest.Definition, grevId);
                results.Add(new InstalledAppEntry(
                    manifest,
                    resolved.BinaryRoot,
                    resolved.DataRoot,
                    true,
                    null));
            }
            catch (InvalidOperationException ex)
            {
                var binaryRoot = manifest.Definition.InstallStrategy == InstallStrategy.GrevIdPortable &&
                                 !string.IsNullOrWhiteSpace(manifest.OwnerGrevId)
                    ? _paths.GetProfileAppRoot(manifest.OwnerGrevId, manifest.Definition.AppId)
                    : _paths.GetGlobalAppRoot(manifest.Definition.AppId);

                results.Add(new InstalledAppEntry(
                    manifest,
                    binaryRoot,
                    null,
                    false,
                    ex.Message));
            }
        }

        return results
            .OrderBy(entry => entry.Manifest.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task RegisterInstalledAsync(
        AppDefinition definition,
        string version,
        string? ownerGrevId,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinition(definition);

        if (string.IsNullOrWhiteSpace(version) || version.Length > 40)
        {
            throw new ArgumentException("Installed app version must be between 1 and 40 characters.", nameof(version));
        }

        if (definition.InstallStrategy == InstallStrategy.GrevIdPortable && string.IsNullOrWhiteSpace(ownerGrevId))
        {
            throw new InvalidOperationException("A GrevID-portable app must have an owning GrevID.");
        }

        if (definition.InstallStrategy != InstallStrategy.GrevIdPortable)
        {
            ownerGrevId = null;
        }

        var root = definition.InstallStrategy == InstallStrategy.GrevIdPortable
            ? _paths.GetProfileAppRoot(ownerGrevId!, definition.AppId)
            : _paths.GetGlobalAppRoot(definition.AppId);

        Directory.CreateDirectory(root);

        var manifest = new InstalledAppManifest(
            definition,
            version.Trim(),
            DateTimeOffset.UtcNow,
            ownerGrevId);

        await WriteManifestAsync(Path.Combine(root, InstalledManifestName), manifest, cancellationToken);
        await _catalogue.UpsertAsync(definition, cancellationToken);
    }

    private async Task<IReadOnlyList<InstalledAppManifest>> ReadManifestsUnderAsync(
        string root,
        string? expectedOwnerGrevId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<InstalledAppManifest>();
        }

        var manifests = new List<InstalledAppManifest>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(directory, InstalledManifestName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<InstalledAppManifest>(
                    stream,
                    _jsonOptions,
                    cancellationToken);

                if (manifest?.Definition is null)
                {
                    continue;
                }

                ValidateDefinition(manifest.Definition);

                if (!string.Equals(Path.GetFileName(directory), manifest.Definition.AppId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isGrevIdLocal = manifest.Definition.InstallStrategy == InstallStrategy.GrevIdPortable;
                if (expectedOwnerGrevId is null)
                {
                    if (isGrevIdLocal || manifest.OwnerGrevId is not null)
                    {
                        continue;
                    }
                }
                else if (!isGrevIdLocal ||
                         !string.Equals(manifest.OwnerGrevId, expectedOwnerGrevId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                manifests.Add(manifest);
            }
            catch (JsonException)
            {
                // One damaged app manifest must not prevent the rest of the Library from loading.
            }
            catch (ArgumentException)
            {
                // Invalid app identities are ignored rather than trusted as paths.
            }
        }

        return manifests;
    }

    private static void ValidateDefinition(AppDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AppIdentity.ValidateAppId(definition.AppId);

        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Launch is null || string.IsNullOrWhiteSpace(definition.Launch.Executable))
        {
            throw new ArgumentException("Installed app manifest contains an invalid app definition.", nameof(definition));
        }
    }

    private async Task WriteManifestAsync(
        string path,
        InstalledAppManifest manifest,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, _jsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
