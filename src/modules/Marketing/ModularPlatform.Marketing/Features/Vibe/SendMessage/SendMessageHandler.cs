using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Messaging;
using ModularPlatform.Marketing.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Marketing.Features.Vibe.SendMessage;

/// <summary>
/// 202 accept for a vibe-chat turn (canonical long-running pattern, mirrors <c>TriggerPullHandler</c>): verifies the
/// conversation belongs to the caller, persists the USER <see cref="VibeMessage"/> AND publishes the durable
/// <see cref="RunVibeAgentTurn"/> work message in ONE outbox transaction. The Worker runs the agent loop and persists
/// the assistant reply; the client learns of it via the <c>marketing.vibe_message_ready</c> realtime event (or by
/// re-fetching the conversation). The slow LLM call never runs here.
/// </summary>
internal sealed class SendMessageHandler(IDbContextOutbox<MarketingDbContext> outbox, IClock clock)
    : ICommandHandler<SendMessageCommand, SendMessageResponse>
{
    public async Task<SendMessageResponse> Handle(SendMessageCommand command, CancellationToken ct)
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
        var message = new VibeMessage
        {
            UserId = command.UserId,
            ConversationId = command.ConversationId,
            Role = "user",
            Content = command.Content,
            CreatedAt = now,
        };
        db.VibeMessages.Add(message);
        conversation.LastMessageAt = now;

        await outbox.PublishAsync(new RunVibeAgentTurn(command.ConversationId, command.UserId));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return new SendMessageResponse(command.ConversationId, message.Id);
    }
}
