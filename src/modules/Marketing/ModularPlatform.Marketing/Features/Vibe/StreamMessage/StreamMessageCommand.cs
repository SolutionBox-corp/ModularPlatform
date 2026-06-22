using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Integrations;

namespace ModularPlatform.Marketing.Features.Vibe.StreamMessage;

/// <summary>
/// Step 1 of the INTERACTIVE streaming send (distinct from the durable 202 <c>SendMessage</c> path): verifies the
/// conversation belongs to the caller, persists the USER turn in its own transaction, and returns the full thread
/// history so the endpoint can run the LLM IN the request and stream the assistant's text deltas as SSE.
/// </summary>
internal sealed record BeginStreamMessageCommand(Guid ConversationId, Guid UserId, string Content)
    : ICommand<BeginStreamMessageResult>;

/// <summary>The persisted user turn + the full conversation history (incl. the just-saved user turn) to feed the agent.</summary>
internal sealed record BeginStreamMessageResult(Guid UserMessageId, IReadOnlyList<VibeTurnInput> History);

/// <summary>
/// Step 2: persists the ASSISTANT turn AFTER the stream completes (Content = the accumulated full text; the streamed
/// path has no tool-call trace, so <c>ToolCallsJson</c> is always null). Dispatched with a disconnect-tolerant token
/// so a dropped client still saves what was generated.
/// </summary>
internal sealed record CompleteStreamMessageCommand(Guid ConversationId, Guid UserId, string FullText)
    : ICommand<CompleteStreamMessageResult>;

internal sealed record CompleteStreamMessageResult(Guid AssistantMessageId);

/// <summary>Wire request — just the message text (mirrors <c>SendMessageRequest</c>).</summary>
public sealed record StreamMessageRequest(string Content);
