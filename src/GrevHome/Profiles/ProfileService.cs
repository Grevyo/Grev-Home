using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Profiles;

public sealed class ProfileService
{
    public const int MaxDisplayNameLength = 50;
    public const int MaxGrevIdLength = 58;

    private const string GrevIdAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int GrevIdPrefixLength = 4;
    private const int GrevIdSuffixLength = 3;
    private const int MaxGrevIdAttempts = 64;

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
                if (profile is null || !string.Equals(folderName, profile.GrevId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                profiles.Add(profile);
                _paths.EnsureProfileLayout(profile.GrevId);
            }
            catch (JsonException)
            {
                // A damaged profile must not prevent the rest of Grev Home from reaching Login.
                // Recovery/import UI will handle repairable profile data later.
            }
            catch (ArgumentException)
            {
                // Invalid or legacy profile-folder identities are ignored rather than trusted as paths.
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

        var grevId = CreateUniqueGrevId(displayName, existing);
        var profile = new LocalProfile(grevId, displayName, DateTimeOffset.UtcNow);
        var profileRoot = _paths.GetProfileRoot(profile.GrevId);

        try
        {
            _paths.EnsureProfileLayout(profile.GrevId);
            await WriteMetadataAsync(profile, cancellationToken);
            return profile;
        }
        catch
        {
            TryDeleteDirectory(profileRoot);
            throw;
        }
    }

    private string CreateUniqueGrevId(string displayName, IReadOnlyCollection<LocalProfile> existing)
    {
        var usernamePart = CreateFilesystemSafeUsernamePart(displayName);

        for (var attempt = 0; attempt < MaxGrevIdAttempts; attempt++)
        {
            var grevId = $"G{CreateRandomToken(GrevIdPrefixLength)}{usernamePart}{CreateRandomToken(GrevIdSuffixLength)}";
            var alreadyKnown = existing.Any(profile =>
                string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase));

            if (!alreadyKnown && !Directory.Exists(_paths.GetProfileRoot(grevId)))
            {
                return grevId;
            }
        }

        throw new IOException("Grev Home could not generate a unique GrevID. Try creating the account again.");
    }

    private static string CreateFilesystemSafeUsernamePart(string displayName)
    {
        var builder = new StringBuilder(MaxDisplayNameLength);
        var previousWasSeparator = false;

        foreach (var character in displayName)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (char.IsWhiteSpace(character) || character is '-' or '_')
            {
                if (builder.Length > 0 && !previousWasSeparator)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
            }
        }

        var safeName = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(safeName) ? "User" : safeName;
    }

    private static string CreateRandomToken(int length)
    {
        Span<char> token = stackalloc char[length];
        for (var index = 0; index < token.Length; index++)
        {
            token[index] = GrevIdAlphabet[RandomNumberGenerator.GetInt32(GrevIdAlphabet.Length)];
        }

        return new string(token);
    }

    private async Task WriteMetadataAsync(LocalProfile profile, CancellationToken cancellationToken)
    {
        var metadataPath = _paths.GetProfileMetadata(profile.GrevId);
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

        if (displayName.Length > MaxDisplayNameLength)
        {
            throw new InvalidOperationException($"Profile names must be {MaxDisplayNameLength} characters or fewer.");
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
