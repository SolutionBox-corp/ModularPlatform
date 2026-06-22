using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.GetConversation;

/// <summary>
/// Reads one conversation + its messages ordered by <c>CreatedAt</c> for the owner. Owner-scoped by the explicit
/// <c>UserId</c> predicate and RLS; a foreign / deleted / missing id is a 404. Messages are read with their own
/// owner predicate so RLS is satisfied on the read context.
/// </summary>
internal sealed class GetConversationHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<GetConversationQuery, ConversationDetail>
{
    public async Task<ConversationDetail> Handle(GetConversationQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var conversation = await db.VibeConversations
            .Where(c => c.Id == query.ConversationId && c.UserId == query.UserId)
            .Select(c => new { c.Id, c.Title, c.CreatedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("marketing.vibe.conversation_not_found", "Conversation not found.");

        var messages = await db.VibeMessages
            .Where(m => m.ConversationId == query.ConversationId && m.UserId == query.UserId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ConversationMessage(m.Id, m.Role, m.Content, m.ToolCallsJson, m.CreatedAt))
            .ToListAsync(ct);

        return new ConversationDetail(conversation.Id, conversation.Title, conversation.CreatedAt, messages);
    }
}
