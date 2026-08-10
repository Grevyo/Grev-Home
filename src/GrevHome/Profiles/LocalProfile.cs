namespace GrevHome.Profiles;

public sealed record LocalProfile(
    Guid Id,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);
