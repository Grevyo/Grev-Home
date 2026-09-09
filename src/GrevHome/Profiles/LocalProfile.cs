namespace GrevHome.Profiles;

public enum AccountRole
{
    Admin,
    Standard,
    Guest
}

public sealed record LocalProfile(
    string GrevId,
    string Username,
    string DisplayName,
    DateTimeOffset CreatedAtUtc,
    AccountRole Role = AccountRole.Admin,
    string AvatarKey = ProfileAvatarCatalog.DefaultKey,
    string? AvatarImageFile = null,
    string Bio = "",
    string StatusMessage = "",
    bool IsBuiltInGuest = false,
    string? PasswordSalt = null,
    string? PasswordHash = null,
    int PasswordIterations = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasControllerPassword => !string.IsNullOrWhiteSpace(PasswordSalt) && !string.IsNullOrWhiteSpace(PasswordHash) && PasswordIterations >= 100_000;
}
