using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Vibe.SendMessage;

/// <summary>
/// Posts a user message to a vibe-chat conversation. Persists the user turn and kicks off the durable agent turn
/// (202 pattern — the LLM loop never runs in the request). Owner from the token; the conversation must belong to them.
/// </summary>
public sealed record SendMessageCommand(Guid ConversationId, Guid UserId, string Content) : ICommand<SendMessageResponse>;

public sealed record SendMessageResponse(Guid ConversationId, Guid MessageId);

/// <summary>Wire request — just the message text.</summary>
public sealed record SendMessageRequest(string Content);
