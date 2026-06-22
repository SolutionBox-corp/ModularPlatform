using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Vibe.GetConversation;

/// <summary>Reads one vibe-chat conversation (header + ordered messages) for the caller who owns it.</summary>
public sealed record GetConversationQuery(Guid ConversationId, Guid UserId) : IQuery<ConversationDetail>;

public sealed record ConversationDetail(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ConversationMessage> Messages);

public sealed record ConversationMessage(
    Guid Id,
    string Role,
    string Content,
    string? ToolCallsJson,
    DateTimeOffset CreatedAt);
