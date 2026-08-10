namespace GrevHome.Profiles;

public sealed record LocalProfile(
    string GrevId,
    string Username,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);
