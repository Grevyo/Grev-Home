using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Profiles;

public sealed class ProfileService
{
    public const int MaxUsernameLength = 50;
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

                // Pre-release metadata created before Username existed is upgraded in-place.
                if (string.IsNullOrWhiteSpace(profile.Username))
                {
                    profile = profile with { Username = profile.DisplayName };
                    await WriteMetadataAsync(profile, cancellationToken);
                }

                profiles.Add(profile);
                _paths.EnsureProfileLayout(profile.GrevId);
            }
            catch (JsonException)
            {
                // A damaged profile must not prevent the rest of Grev Home from reaching Login.
            }
            catch (ArgumentException)
            {
                // Invalid or legacy profile-folder identities are ignored rather than trusted as paths.
            }
        }

        return profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Username, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<LocalProfile> CreateAsync(
        string username,
        AccountRole role,
        CancellationToken cancellationToken = default)
    {
        username = ValidateUsername(username);
        var existing = await GetProfilesAsync(cancellationToken);

        if (existing.Any(profile => string.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A local account with username '{username}' already exists.");
        }

        var grevId = CreateUniqueGrevId(username, existing);
        var profile = new LocalProfile(grevId, username, username, DateTimeOffset.UtcNow, role);
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

    public async Task<LocalProfile> UpdateDisplayNameAsync(
        string grevId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        displayName = ValidateDisplayName(displayName);
        var profiles = await GetProfilesAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("That local account does not exist.");

        var updated = profile with { DisplayName = displayName };
        await WriteMetadataAsync(updated, cancellationToken);
        return updated;
    }

    private string CreateUniqueGrevId(string username, IReadOnlyCollection<LocalProfile> existing)
    {
        var usernamePart = CreateFilesystemSafeUsernamePart(username);

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

    private static string CreateFilesystemSafeUsernamePart(string username)
    {
        var builder = new StringBuilder(MaxUsernameLength);
        var previousWasSeparator = false;

        foreach (var character in username)
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

    private static string ValidateUsername(string username)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Enter a username.");
        }

        if (username.Length > MaxUsernameLength)
        {
            throw new InvalidOperationException($"Usernames must be {MaxUsernameLength} characters or fewer.");
        }

        if (username.Any(char.IsControl))
        {
            throw new InvalidOperationException("Usernames cannot contain control characters.");
        }

        return username;
    }

    private static string ValidateDisplayName(string displayName)
    {
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Enter a display name.");
        }

        if (displayName.Length > MaxDisplayNameLength)
        {
            throw new InvalidOperationException($"Display names must be {MaxDisplayNameLength} characters or fewer.");
        }

        if (displayName.Any(char.IsControl))
        {
            throw new InvalidOperationException("Display names cannot contain control characters.");
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
