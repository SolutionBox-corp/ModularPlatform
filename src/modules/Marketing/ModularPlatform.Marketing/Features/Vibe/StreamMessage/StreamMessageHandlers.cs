using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Integrations;
using ModularPlatform.Marketing.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Marketing.Features.Vibe.StreamMessage;

/// <summary>
/// Persists the USER turn for an interactive streaming send and returns the thread history. NO durable work is
/// published here (unlike <c>SendMessageHandler</c>): the assistant turn is produced by the LLM running IN the request
/// and persisted by <see cref="CompleteStreamMessageHandler"/> after the stream finishes — so the durable
/// <c>RunVibeAgentTurn</c> path is intentionally NOT triggered (it would double-answer).
/// </summary>
internal sealed class BeginStreamMessageHandler(IDbContextOutbox<MarketingDbContext> outbox, IClock clock)
    : ICommandHandler<BeginStreamMessageCommand, BeginStreamMessageResult>
{
    public async Task<BeginStreamMessageResult> Handle(BeginStreamMessageCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        // RLS already isolates the caller's rows; the explicit owner predicate makes the 404 deterministic.
        var conversation = await db.VibeConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId && c.UserId == command.UserId, ct);
        if (conversation is null)
        {
            throw new NotFoundException("marketing.vibe.conversation_not_found", "Conversation not found.");
        }

        var now = clock.UtcNow;
        var userMessage = new VibeMessage
        {
            UserId = command.UserId,
            ConversationId = command.ConversationId,
            Role = "user",
            Content = command.Content,
            CreatedAt = now,
        };
        db.VibeMessages.Add(userMessage);
        conversation.LastMessageAt = now;
        await db.SaveChangesAsync(ct);

        var history = await db.VibeMessages
            .Where(m => m.ConversationId == command.ConversationId && m.UserId == command.UserId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new VibeTurnInput(m.Role, m.Content))
            .ToListAsync(ct);

        return new BeginStreamMessageResult(userMessage.Id, history);
    }
}

/// <summary>
/// Persists the ASSISTANT turn after the streamed LLM run completes. Runs in its own transaction; dispatched with a
/// disconnect-tolerant token so a dropped client still saves what was generated. The streamed path carries no
/// tool-call trace, so <c>ToolCallsJson</c> is null. Re-verifies ownership defensively (RLS + explicit predicate).
/// </summary>
internal sealed class CompleteStreamMessageHandler(IDbContextOutbox<MarketingDbContext> outbox, IClock clock)
    : ICommandHandler<CompleteStreamMessageCommand, CompleteStreamMessageResult>
{
    public async Task<CompleteStreamMessageResult> Handle(CompleteStreamMessageCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        var conversation = await db.VibeConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId && c.UserId == command.UserId, ct);
        if (conversation is null)
        {
            throw new NotFoundException("marketing.vibe.conversation_not_found", "Conversation not found.");
        }

        var now = clock.UtcNow;
        var assistantMessage = new VibeMessage
        {
            UserId = command.UserId,
            ConversationId = command.ConversationId,
            Role = "assistant",
            Content = command.FullText,
            ToolCallsJson = null,
            CreatedAt = now,
        };
        db.VibeMessages.Add(assistantMessage);
        conversation.LastMessageAt = now;
        await db.SaveChangesAsync(ct);

        return new CompleteStreamMessageResult(assistantMessage.Id);
    }
}
