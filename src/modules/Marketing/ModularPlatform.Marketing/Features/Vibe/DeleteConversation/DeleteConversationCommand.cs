using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Web;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Marketing.Features.Vibe.DeleteConversation;

/// <summary>Soft-deletes one of the caller's vibe-chat conversations. Owner from the token; 404 for anyone else.</summary>
public sealed record DeleteConversationCommand(Guid ConversationId, Guid UserId) : ICommand;

/// <summary>
/// Soft-deletes a conversation by stamping <c>DeletedAt</c> on the TRACKED entity; the entity's <c>DeletedAt == null</c>
/// query filter then hides it from the sidebar while history is retained. Tracked write → xmin serializes, audit recorded.
/// A foreign / missing / already-deleted id is a 404 (the soft-delete filter excludes deleted rows from the load).
/// </summary>
internal sealed class DeleteConversationHandler(IDbContextOutbox<MarketingDbContext> outbox, IClock clock)
    : ICommandHandler<DeleteConversationCommand>
{
    public async Task<Unit> Handle(DeleteConversationCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        var conversation = await db.VibeConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("marketing.vibe.conversation_not_found", "Conversation not found.");

        conversation.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
