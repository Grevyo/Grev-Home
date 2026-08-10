using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Profiles;

public sealed class ProfileService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ProfileService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<LocalProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureMachineLayout();
        var profiles = new List<LocalProfile>();

        foreach (var directory in Directory.EnumerateDirectories(_paths.Profiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(directory);
            if (folderName.StartsWith('_'))
            {
                continue;
            }

            var metadataPath = Path.Combine(directory, "profile.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var profile = await JsonSerializer.DeserializeAsync<LocalProfile>(stream, _jsonOptions, cancellationToken);
                if (profile is not null)
                {
                    profiles.Add(profile);
                    _paths.EnsureProfileLayout(profile.Id);
                }
            }
            catch (JsonException)
            {
                // A damaged profile must not prevent the rest of Grev Home from reaching Login.
                // Recovery/repair UI will be added as a dedicated management flow later.
            }
        }

        return profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<LocalProfile> CreateAsync(string displayName, CancellationToken cancellationToken = default)
    {
        displayName = ValidateDisplayName(displayName);
        var existing = await GetProfilesAsync(cancellationToken);

        if (existing.Any(profile => string.Equals(profile.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A local profile named '{displayName}' already exists.");
        }

        var profile = new LocalProfile(Guid.NewGuid(), displayName, DateTimeOffset.UtcNow);
        var profileRoot = _paths.GetProfileRoot(profile.Id);

        try
        {
            _paths.EnsureProfileLayout(profile.Id);
            await WriteMetadataAsync(profile, cancellationToken);
            return profile;
        }
        catch
        {
            TryDeleteDirectory(profileRoot);
            throw;
        }
    }

    private async Task WriteMetadataAsync(LocalProfile profile, CancellationToken cancellationToken)
    {
        var metadataPath = _paths.GetProfileMetadata(profile.Id);
        var temporaryPath = metadataPath + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, profile, _jsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ValidateDisplayName(string displayName)
    {
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Enter a profile name.");
        }

        if (displayName.Length > 32)
        {
            throw new InvalidOperationException("Profile names must be 32 characters or fewer.");
        }

        if (displayName.Any(char.IsControl))
        {
            throw new InvalidOperationException("Profile names cannot contain control characters.");
        }

        return displayName;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort rollback. Leaving recoverable data is safer than masking the original failure.
        }
    }
}
