using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Features.Vibe.ListConversations;

/// <summary>
/// Lists the caller's vibe-chat conversations, newest first. Owner-scoped by the explicit <c>WHERE UserId</c> and RLS;
/// the soft-delete query filter on the entity hides deleted threads automatically.
/// </summary>
internal sealed class ListConversationsHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<ListConversationsQuery, PagedResponse<ConversationListItem>>
{
    public async Task<PagedResponse<ConversationListItem>> Handle(ListConversationsQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        return await db.VibeConversations
            .Where(c => c.UserId == query.UserId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new ConversationListItem(c.Id, c.Title, c.CreatedAt, c.LastMessageAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
