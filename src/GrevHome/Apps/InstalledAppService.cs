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
    private const int AppLibraryPreferencesVersion = 1;

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
        var removedGlobalAppIds = await ReadRemovedGlobalAppIdsAsync(grevId, cancellationToken);

        var globalManifests = await ReadManifestsUnderAsync(
            _paths.GlobalApps,
            expectedOwnerGrevId: null,
            cancellationToken);
        manifests.AddRange(globalManifests.Where(manifest =>
            !removedGlobalAppIds.Contains(manifest.Definition.AppId)));

        if (!string.IsNullOrWhiteSpace(grevId))
        {
            _paths.EnsureProfileLayout(grevId);
            manifests.AddRange(await ReadManifestsUnderAsync(_paths.GetProfileApps(grevId), grevId, cancellationToken));
        }

        return BuildEntries(manifests, grevId, cancellationToken);
    }

    public async Task<IReadOnlyList<InstalledAppEntry>> GetMachineInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureMachineLayout();
        var manifests = await ReadManifestsUnderAsync(
            _paths.GlobalApps,
            expectedOwnerGrevId: null,
            cancellationToken);
        return BuildEntries(manifests, grevId: null, cancellationToken);
    }

    public async Task<bool> IsGlobalAppInUserLibraryAsync(
        string? grevId,
        string appId,
        CancellationToken cancellationToken = default)
    {
        AppIdentity.ValidateAppId(appId);
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return true;
        }

        var removed = await ReadRemovedGlobalAppIdsAsync(grevId, cancellationToken);
        return !removed.Contains(appId);
    }

    public async Task RemoveGlobalAppFromUserLibraryAsync(
        string grevId,
        string appId,
        CancellationToken cancellationToken = default)
    {
        AppIdentity.ValidateAppId(appId);
        _paths.EnsureProfileLayout(grevId);

        var machineInstalled = await GetMachineInstalledAsync(cancellationToken);
        if (machineInstalled.All(entry =>
                !string.Equals(entry.Manifest.Definition.AppId, appId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That Global App is not installed on this Grev Machine.");
        }

        var removed = await ReadRemovedGlobalAppIdsAsync(grevId, cancellationToken);
        if (!removed.Add(appId))
        {
            return;
        }

        await WriteAppLibraryPreferencesAsync(grevId, removed, cancellationToken);
    }

    public async Task RestoreGlobalAppToUserLibraryAsync(
        string grevId,
        string appId,
        CancellationToken cancellationToken = default)
    {
        AppIdentity.ValidateAppId(appId);
        _paths.EnsureProfileLayout(grevId);

        var removed = await ReadRemovedGlobalAppIdsAsync(grevId, cancellationToken);
        if (!removed.Remove(appId))
        {
            return;
        }

        await WriteAppLibraryPreferencesAsync(grevId, removed, cancellationToken);
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

    private IReadOnlyList<InstalledAppEntry> BuildEntries(
        IEnumerable<InstalledAppManifest> manifests,
        string? grevId,
        CancellationToken cancellationToken)
    {
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

    private async Task<HashSet<string>> ReadRemovedGlobalAppIdsAsync(
        string? grevId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        _paths.EnsureProfileLayout(grevId);
        var path = _paths.GetProfileAppLibraryPreferencesFile(grevId);
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var preferences = await JsonSerializer.DeserializeAsync<AppLibraryPreferences>(
                stream,
                _jsonOptions,
                cancellationToken);

            if (preferences is null || preferences.Version != AppLibraryPreferencesVersion)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var appId in preferences.RemovedGlobalAppIds ?? Array.Empty<string>())
            {
                try
                {
                    removed.Add(AppIdentity.ValidateAppId(appId));
                }
                catch (ArgumentException)
                {
                    // Ignore damaged preference entries without hiding any unrelated apps.
                }
            }

            return removed;
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteAppLibraryPreferencesAsync(
        string grevId,
        HashSet<string> removedGlobalAppIds,
        CancellationToken cancellationToken)
    {
        var path = _paths.GetProfileAppLibraryPreferencesFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var preferences = new AppLibraryPreferences(
            AppLibraryPreferencesVersion,
            removedGlobalAppIds.OrderBy(appId => appId, StringComparer.OrdinalIgnoreCase).ToArray());

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, _jsonOptions, cancellationToken);
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

    private sealed record AppLibraryPreferences(
        int Version,
        IReadOnlyList<string> RemovedGlobalAppIds);
}
