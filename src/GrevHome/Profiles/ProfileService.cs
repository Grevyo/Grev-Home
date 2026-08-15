using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using GrevHome.Storage;

namespace GrevHome.Profiles;

public sealed class ProfileService
{
    public const int MaxUsernameLength = 50;
    public const int MaxDisplayNameLength = 50;
    public const int MaxBioLength = 160;
    public const int MaxStatusMessageLength = 60;
    public const int MaxGrevIdLength = 58;
    public const long MaxAvatarFileBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> SupportedAvatarExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp" };

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
            if (folderName.StartsWith('_')) continue;

            var metadataPath = Path.Combine(directory, "profile.json");
            if (!File.Exists(metadataPath)) continue;

            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var profile = await JsonSerializer.DeserializeAsync<LocalProfile>(stream, _jsonOptions, cancellationToken);
                if (profile is null || !string.Equals(folderName, profile.GrevId, StringComparison.OrdinalIgnoreCase)) continue;

                var needsUpgrade = false;
                if (string.IsNullOrWhiteSpace(profile.Username) && string.IsNullOrWhiteSpace(profile.DisplayName))
                {
                    // Do not invent permanent identity for a damaged profile. Leaving it out of Login is
                    // safer than creating a username/display-name value that was never actually chosen.
                    continue;
                }

                if (string.IsNullOrWhiteSpace(profile.Username))
                {
                    profile = profile with { Username = profile.DisplayName };
                    needsUpgrade = true;
                }

                if (string.IsNullOrWhiteSpace(profile.DisplayName))
                {
                    profile = profile with { DisplayName = profile.Username };
                    needsUpgrade = true;
                }

                if (profile.Bio is null)
                {
                    profile = profile with { Bio = string.Empty };
                    needsUpgrade = true;
                }

                if (profile.StatusMessage is null)
                {
                    profile = profile with { StatusMessage = string.Empty };
                    needsUpgrade = true;
                }

                var avatarKey = ProfileAvatarCatalog.Normalize(profile.AvatarKey);
                if (avatarKey == ProfileAvatarCatalog.CustomKey && string.IsNullOrWhiteSpace(profile.AvatarImageFile))
                {
                    avatarKey = ProfileAvatarCatalog.DefaultKey;
                }

                if (!string.Equals(avatarKey, profile.AvatarKey, StringComparison.OrdinalIgnoreCase))
                {
                    profile = profile with
                    {
                        AvatarKey = avatarKey,
                        AvatarImageFile = avatarKey == ProfileAvatarCatalog.CustomKey ? profile.AvatarImageFile : null
                    };
                    needsUpgrade = true;
                }

                if (needsUpgrade) await WriteMetadataAsync(profile, cancellationToken);

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
            catch (IOException)
            {
                // One unreadable/locked profile must not take down every other local account at Login.
            }
            catch (UnauthorizedAccessException)
            {
                // Treat an inaccessible profile as unavailable rather than crashing the Grev Home shell.
            }
        }

        return profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Username, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<LocalProfile> CreateAsync(string username, AccountRole role, CancellationToken cancellationToken = default)
    {
        username = ValidateUsername(username);
        var existing = await GetProfilesAsync(cancellationToken);
        if (existing.Any(profile => string.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A local account with username '{username}' already exists.");
        }

        if (existing.Count == 0) role = AccountRole.Admin;

        var grevId = CreateUniqueGrevId(username, existing);
        var profile = new LocalProfile(grevId, username, username, DateTimeOffset.UtcNow, role, ProfileAvatarCatalog.DefaultKey);
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

    public async Task<LocalProfile> UpdateDisplayNameAsync(string grevId, string displayName, CancellationToken cancellationToken = default)
    {
        displayName = ValidateDisplayName(displayName);
        var profile = await GetRequiredProfileAsync(grevId, cancellationToken);
        var updated = profile with { DisplayName = displayName };
        await WriteMetadataAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<LocalProfile> UpdateAvatarAsync(string grevId, string avatarKey, CancellationToken cancellationToken = default)
    {
        var profile = await GetRequiredProfileAsync(grevId, cancellationToken);
        var normalized = ProfileAvatarCatalog.Normalize(avatarKey);
        if (normalized == ProfileAvatarCatalog.CustomKey && string.IsNullOrWhiteSpace(profile.AvatarImageFile))
        {
            throw new InvalidOperationException("Choose a custom profile photo first.");
        }

        var updated = profile with
        {
            AvatarKey = normalized,
            AvatarImageFile = normalized == ProfileAvatarCatalog.CustomKey ? profile.AvatarImageFile : null
        };
        await WriteMetadataAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<LocalProfile> UpdateRoleAsync(string grevId, AccountRole role, CancellationToken cancellationToken = default)
    {
        var profiles = await GetProfilesAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(candidate => string.Equals(candidate.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("That local account does not exist.");
        EnsureRoleChangeIsSafe(profile, role, profiles);
        var updated = profile with { Role = role };
        await WriteMetadataAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<LocalProfile> UpdateProfileAsync(
        string grevId,
        string displayName,
        string avatarKey,
        AccountRole? newRole,
        string? customAvatarSourcePath = null,
        string? bio = null,
        string? statusMessage = null,
        CancellationToken cancellationToken = default)
    {
        displayName = ValidateDisplayName(displayName);
        var profiles = await GetProfilesAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(candidate => string.Equals(candidate.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("That local account does not exist.");

        var normalizedBio = bio is null ? profile.Bio : ValidateBio(bio);
        var normalizedStatusMessage = statusMessage is null ? profile.StatusMessage : ValidateStatusMessage(statusMessage);
        var role = newRole ?? profile.Role;
        EnsureRoleChangeIsSafe(profile, role, profiles);

        var normalizedAvatar = ProfileAvatarCatalog.Normalize(avatarKey);
        var avatarImageFile = profile.AvatarImageFile;
        var previousAvatarFile = profile.AvatarImageFile;

        if (normalizedAvatar == ProfileAvatarCatalog.CustomKey)
        {
            if (!string.IsNullOrWhiteSpace(customAvatarSourcePath))
            {
                avatarImageFile = ImportAvatarImage(profile.GrevId, customAvatarSourcePath);
            }
            else if (string.IsNullOrWhiteSpace(avatarImageFile) ||
                     !File.Exists(Path.Combine(_paths.GetProfileRoot(profile.GrevId), Path.GetFileName(avatarImageFile))))
            {
                throw new InvalidOperationException("Choose a custom profile photo first.");
            }
        }
        else
        {
            avatarImageFile = null;
        }

        var updated = profile with
        {
            DisplayName = displayName,
            Bio = normalizedBio,
            StatusMessage = normalizedStatusMessage,
            AvatarKey = normalizedAvatar,
            AvatarImageFile = avatarImageFile,
            Role = role
        };
        await WriteMetadataAsync(updated, cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousAvatarFile) &&
            !string.Equals(previousAvatarFile, avatarImageFile, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteAvatarFile(profile.GrevId, previousAvatarFile);
        }

        return updated;
    }

    private string ImportAvatarImage(string grevId, string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("That profile photo no longer exists.", source);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!SupportedAvatarExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Profile photos must be PNG, JPG, JPEG or BMP images.");
        }

        var info = new FileInfo(source);
        if (info.Length <= 0 || info.Length > MaxAvatarFileBytes)
        {
            throw new InvalidOperationException("Profile photos must be larger than 0 bytes and no more than 10 MB.");
        }

        try
        {
            using var validationStream = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(validationStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidOperationException("That file does not contain a readable image.");
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            throw new InvalidOperationException("That file is not a readable profile image.", ex);
        }

        var profileRoot = _paths.GetProfileRoot(grevId);
        Directory.CreateDirectory(profileRoot);
        var fileName = $"avatar{extension}";
        var target = Path.Combine(profileRoot, fileName);
        var temporary = Path.Combine(profileRoot, $"avatar-upload-{Guid.NewGuid():N}{extension}.tmp");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return fileName;
    }

    private void TryDeleteAvatarFile(string grevId, string avatarImageFile)
    {
        try
        {
            var fileName = Path.GetFileName(avatarImageFile);
            if (fileName.StartsWith("avatar", StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(_paths.GetProfileRoot(grevId), fileName);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        catch
        {
            // A stale old avatar is harmless; never fail a successful profile update because cleanup failed.
        }
    }

    private static void EnsureRoleChangeIsSafe(LocalProfile profile, AccountRole role, IReadOnlyCollection<LocalProfile> profiles)
    {
        if (profile.Role == AccountRole.Admin && role != AccountRole.Admin &&
            profiles.Count(candidate => candidate.Role == AccountRole.Admin) <= 1)
        {
            throw new InvalidOperationException("Grev Home must always have at least one Admin account.");
        }
    }

    private async Task<LocalProfile> GetRequiredProfileAsync(string grevId, CancellationToken cancellationToken)
    {
        var profiles = await GetProfilesAsync(cancellationToken);
        return profiles.FirstOrDefault(candidate => string.Equals(candidate.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException("That local account does not exist.");
    }

    private string CreateUniqueGrevId(string username, IReadOnlyCollection<LocalProfile> existing)
    {
        var usernamePart = CreateFilesystemSafeUsernamePart(username);
        for (var attempt = 0; attempt < MaxGrevIdAttempts; attempt++)
        {
            var grevId = $"G{CreateRandomToken(GrevIdPrefixLength)}{usernamePart}{CreateRandomToken(GrevIdSuffixLength)}";
            var alreadyKnown = existing.Any(profile => string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
            if (!alreadyKnown && !Directory.Exists(_paths.GetProfileRoot(grevId))) return grevId;
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
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string ValidateUsername(string username)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Enter a username.");
        if (username.Length > MaxUsernameLength) throw new InvalidOperationException($"Usernames must be {MaxUsernameLength} characters or fewer.");
        if (username.Any(char.IsControl)) throw new InvalidOperationException("Usernames cannot contain control characters.");
        return username;
    }

    private static string ValidateDisplayName(string displayName)
    {
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) throw new InvalidOperationException("Enter a display name.");
        if (displayName.Length > MaxDisplayNameLength) throw new InvalidOperationException($"Display names must be {MaxDisplayNameLength} characters or fewer.");
        if (displayName.Any(char.IsControl)) throw new InvalidOperationException("Display names cannot contain control characters.");
        return displayName;
    }

    private static string ValidateBio(string bio)
    {
        bio = bio.Trim();
        if (bio.Length > MaxBioLength) throw new InvalidOperationException($"Profile bios must be {MaxBioLength} characters or fewer.");
        if (bio.Any(char.IsControl)) throw new InvalidOperationException("Profile bios cannot contain control characters.");
        return bio;
    }

    private static string ValidateStatusMessage(string statusMessage)
    {
        statusMessage = statusMessage.Trim();
        if (statusMessage.Length > MaxStatusMessageLength)
        {
            throw new InvalidOperationException($"Profile status messages must be {MaxStatusMessageLength} characters or fewer.");
        }
        if (statusMessage.Any(char.IsControl))
        {
            throw new InvalidOperationException("Profile status messages cannot contain control characters.");
        }
        return statusMessage;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort rollback. Leaving recoverable data is safer than masking the original failure.
        }
    }
}
