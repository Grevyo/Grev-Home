namespace GrevHome.Profiles;

public sealed record LocalProfile(
    string GrevId,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);
