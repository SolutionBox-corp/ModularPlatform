namespace ModularPlatform.Marketing.Messaging;

/// <summary>
/// Durable work message for one vibe-chat agent turn (published by the SendMessage accept handler via the outbox,
/// consumed by the Worker). Intra-module — not a cross-module integration contract. Carries the conversation + owner
/// so the worker can load history under system context and persist the assistant reply for the right user.
/// </summary>
public sealed record RunVibeAgentTurn(Guid ConversationId, Guid UserId);
