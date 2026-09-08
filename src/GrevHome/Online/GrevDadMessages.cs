namespace GrevHome.Online;

public sealed record GrevDadConversation(string RoomId, string UserId, string DisplayName, int Unread);
public sealed record GrevDadInbox(bool Ok, string? Message, IReadOnlyList<GrevDadConversation>? Conversations);

public sealed record GrevDadMessage(string Id, string SenderUserId, string Body, long CreatedAt, string Type);
public sealed record GrevDadMessagePage(bool Ok, string? Message, string? RoomId, IReadOnlyList<GrevDadMessage>? Messages, bool HasMore);
