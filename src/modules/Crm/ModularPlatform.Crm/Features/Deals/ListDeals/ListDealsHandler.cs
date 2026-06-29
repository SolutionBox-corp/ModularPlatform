using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Deals.ListDeals;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS; newest first; bounded page size.</summary>
internal sealed class ListDealsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListDealsQuery, PagedResponse<DealListItem>>
{
    public async Task<PagedResponse<DealListItem>> Handle(ListDealsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);

        var filtered = db.Deals.Where(d => d.UserId == query.UserId);

        if (!string.IsNullOrWhiteSpace(query.Stage))
        {
            var stage = query.Stage.Trim().ToLowerInvariant();
            filtered = filtered.Where(d => d.Stage == stage);
        }

        if (query.ContactId is { } contactId)
        {
            filtered = filtered.Where(d => d.ContactId == contactId);
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(d => d.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(d => new DealListItem(
                d.Id, d.ContactId, d.Title, d.AmountCents, d.Currency, d.Stage, d.ExpectedCloseAt, d.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<DealListItem>(items, paging.Page, paging.PageSize, total);
    }
}
