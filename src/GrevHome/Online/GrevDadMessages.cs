namespace GrevHome.Online;

public sealed record GrevDadMessage(string Id, string SenderUserId, string Body, long CreatedAt, string Type);
public sealed record GrevDadMessagePage(bool Ok, string? Message, string? RoomId, IReadOnlyList<GrevDadMessage>? Messages, bool HasMore);
