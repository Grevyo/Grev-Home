namespace GrevHome.Profiles;

public sealed record LocalProfile(
    Guid GrevId,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);
