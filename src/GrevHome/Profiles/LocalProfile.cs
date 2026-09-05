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
    bool IsBuiltInGuest = false);
