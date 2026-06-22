using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Vibe.ListConversations;

/// <summary>The caller's non-deleted vibe-chat conversations, newest first.</summary>
public sealed record ListConversationsQuery(Guid UserId) : IQuery<IReadOnlyList<ConversationListItem>>;

public sealed record ConversationListItem(Guid Id, string Title, DateTimeOffset CreatedAt);
