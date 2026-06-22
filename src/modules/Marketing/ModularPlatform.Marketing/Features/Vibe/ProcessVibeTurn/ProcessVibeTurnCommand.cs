using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Integrations;
using ModularPlatform.Marketing.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Marketing.Features.Vibe.ProcessVibeTurn;

/// <summary>Internal work command for one vibe-chat agent turn (dispatched by the durable Worker shell, not over HTTP).</summary>
internal sealed record ProcessVibeTurnCommand(Guid ConversationId, Guid UserId) : ICommand;

/// <summary>
/// Runs ONE agent turn for a conversation: loads the thread's messages (system context on the Worker), builds the
/// history, calls <see cref="IVibeAgentGateway"/>, persists the assistant <see cref="VibeMessage"/> (+ tool-call
/// trace), commits, then — AFTER the commit — fires a realtime "message ready" push (mirrors
/// <c>SendNotificationHandler</c>: a denied/failed write must not emit a phantom event).
/// <para>
/// IDEMPOTENT: a redelivered message must not produce a second assistant reply. If the latest message in the thread is
/// already an assistant turn, the user's turn has been answered → skip. (The user always appends BEFORE publishing, so
/// "latest is assistant" reliably means this turn already ran.)
/// </para>
/// </summary>
internal sealed class ProcessVibeTurnHandler(
    IDbContextOutbox<MarketingDbContext> outbox,
    IVibeAgentGateway agent,
    IRealtimePublisher realtime,
    IClock clock,
    ILogger<ProcessVibeTurnHandler> logger)
    : ICommandHandler<ProcessVibeTurnCommand>
{
    public async Task<Unit> Handle(ProcessVibeTurnCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        var conversation = await db.VibeConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId && c.UserId == command.UserId, ct);
        if (conversation is null)
        {
            logger.LogWarning("Vibe conversation {ConversationId} not found; skipping turn.", command.ConversationId);
            return Unit.Value;
        }

        var messages = await db.VibeMessages
            .Where(m => m.ConversationId == command.ConversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            return Unit.Value;
        }

        // Idempotency guard: a redelivered RunVibeAgentTurn must not double-answer. The user turn is always persisted
        // before the message is published, so a trailing assistant turn means this user turn was already processed.
        if (messages[^1].Role == "assistant")
        {
            return Unit.Value;
        }

        var history = messages
            .Select(m => new VibeTurnInput(m.Role, m.Content))
            .ToList();

        var result = await agent.RunTurnAsync(command.UserId, history, ct);

        db.VibeMessages.Add(new VibeMessage
        {
            UserId = command.UserId,
            ConversationId = command.ConversationId,
            Role = "assistant",
            Content = result.Content,
            ToolCallsJson = result.ToolCallsJson,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        // Non-transactional realtime push AFTER the commit only (mirrors SendNotificationHandler / AnalyzeMarketingData):
        // a failed write must not produce a phantom "message ready" event. Worker path — pushing to the owner is correct.
        await realtime.PublishToUserAsync(
            command.UserId,
            "marketing.vibe_message_ready",
            new { conversationId = command.ConversationId },
            ct);

        return Unit.Value;
    }
}
