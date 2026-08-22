using System.Text.Json.Serialization;

namespace GrevHome.Online;

public enum GrevDadConnectionState
{
    Unlinked,
    Linking,
    Linked,
    Offline,
    Expired,
    Revoked,
    Error
}

public sealed record GrevDadRemoteAccount(
    string UserId,
    string Username,
    string DisplayName,
    bool IsVerified,
    string GrevId,
    string LocalUsername,
    string LocalDisplayName,
    string LinkId);

public sealed record GrevDadAccountSnapshot(
    GrevDadConnectionState State,
    GrevDadRemoteAccount? Account,
    string? Message,
    DateTimeOffset? LastContactAtUtc,
    DateTimeOffset? TokenExpiresAtUtc)
{
    public static GrevDadAccountSnapshot Unlinked { get; } =
        new(GrevDadConnectionState.Unlinked, null, null, null, null);
}

public sealed record GrevDadLinkStart(
    string LinkId,
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAtUtc,
    int PollIntervalSeconds);

public enum GrevDadLinkPollState
{
    Pending,
    Approved,
    Denied,
    Expired,
    Revoked
}

public sealed record GrevDadLinkPollResult(
    GrevDadLinkPollState State,
    GrevDadRemoteAccount? Account,
    string? Message);

public sealed record GrevDadPresence(
    string Availability,
    string StatusText,
    string ActivityType,
    string ActivityText,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record GrevDadFriend(
    string UserId,
    string Username,
    string DisplayName,
    bool IsVerified,
    DateTimeOffset FriendsSinceUtc,
    GrevDadPresence Presence);

public sealed record GrevDadMemberSearchResult(
    string UserId,
    string Username,
    string DisplayName,
    bool IsVerified,
    bool IsFriend,
    bool OutgoingPending,
    bool IncomingPending);

public sealed record GrevDadFriendRequestUser(
    string UserId,
    string Username,
    string DisplayName);

public sealed record GrevDadFriendRequest(
    string Id,
    DateTimeOffset CreatedAtUtc,
    GrevDadFriendRequestUser User);

public sealed record GrevDadFriendRequestsSnapshot(
    IReadOnlyList<GrevDadFriendRequest> Incoming,
    IReadOnlyList<GrevDadFriendRequest> Outgoing)
{
    public static GrevDadFriendRequestsSnapshot Empty { get; } =
        new(Array.Empty<GrevDadFriendRequest>(), Array.Empty<GrevDadFriendRequest>());
}

public sealed record GrevDadActivityEvent(
    string Id,
    GrevDadFriendRequestUser User,
    string Type,
    string AppId,
    string AppName,
    string Detail,
    string Visibility,
    DateTimeOffset OccurredAtUtc);

internal sealed record GrevDadLinkMetadata(
    int ApiVersion,
    GrevDadRemoteAccount Account,
    DateTimeOffset LinkedAtUtc,
    DateTimeOffset TokenExpiresAtUtc,
    DateTimeOffset? LastValidatedAtUtc);

internal sealed record GrevDadPendingCredential(
    string LinkId,
    string DeviceCode,
    DateTimeOffset ExpiresAtUtc,
    int PollIntervalSeconds);

internal sealed record GrevDadCachedData(
    DateTimeOffset CachedAtUtc,
    GrevDadRemoteAccount? Account,
    IReadOnlyList<GrevDadFriend> Friends,
    IReadOnlyList<GrevDadActivityEvent> Activity)
{
    public static GrevDadCachedData Empty { get; } =
        new(DateTimeOffset.MinValue, null, Array.Empty<GrevDadFriend>(), Array.Empty<GrevDadActivityEvent>());
}

internal sealed record ApiEnvelope(
    bool Ok,
    string? Message,
    int? ApiVersion);

internal sealed record LinkStartApiResponse(
    bool Ok,
    string? Message,
    int ApiVersion,
    string LinkId,
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    long ExpiresAt,
    int IntervalSeconds);

internal sealed record LinkStatusApiResponse(
    bool Ok,
    string? Message,
    string Status,
    int? ApiVersion,
    string? AccessToken,
    long? TokenExpiresAt,
    GrevDadRemoteAccount? Account);

internal sealed record AccountApiResponse(
    bool Ok,
    string? Message,
    int ApiVersion,
    GrevDadRemoteAccount? Account);

internal sealed record PresenceApiPayload(
    string Availability,
    string StatusText,
    string ActivityType,
    string ActivityText,
    long? ExpiresAt,
    long? UpdatedAt);

internal sealed record PresenceApiResponse(
    bool Ok,
    string? Message,
    PresenceApiPayload? Presence);

internal sealed record FriendApiPayload(
    string UserId,
    string Username,
    string DisplayName,
    bool IsVerified,
    long FriendsSince,
    PresenceApiPayload Presence);

internal sealed record FriendsApiResponse(
    bool Ok,
    string? Message,
    IReadOnlyList<FriendApiPayload>? Friends);

internal sealed record MemberSearchApiResponse(
    bool Ok,
    string? Message,
    IReadOnlyList<GrevDadMemberSearchResult>? Users);

internal sealed record FriendRequestApiPayload(
    string Id,
    long CreatedAt,
    GrevDadFriendRequestUser User);

internal sealed record FriendRequestsApiResponse(
    bool Ok,
    string? Message,
    IReadOnlyList<FriendRequestApiPayload>? Incoming,
    IReadOnlyList<FriendRequestApiPayload>? Outgoing);

internal sealed record ActivityApiUser(
    string UserId,
    string Username,
    string DisplayName);

internal sealed record ActivityApiPayload(
    string Id,
    ActivityApiUser User,
    string Type,
    string AppId,
    string AppName,
    string Detail,
    string Visibility,
    long OccurredAt);

internal sealed record ActivityApiResponse(
    bool Ok,
    string? Message,
    IReadOnlyList<ActivityApiPayload>? Events);
