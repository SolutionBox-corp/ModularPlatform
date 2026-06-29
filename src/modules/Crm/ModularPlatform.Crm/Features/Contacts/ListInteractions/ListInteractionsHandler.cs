using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.ListInteractions;

/// <summary>Read slice. Owner-scoped by WHERE + RLS; a foreign contact id yields an empty list (leaks nothing).</summary>
internal sealed class ListInteractionsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListInteractionsQuery, PagedResponse<InteractionResponse>>
{
    public async Task<PagedResponse<InteractionResponse>> Handle(ListInteractionsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);

        var filtered = db.ContactInteractions
            .Where(i => i.ContactId == query.ContactId && i.UserId == query.UserId);

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(i => i.OccurredAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(i => new InteractionResponse(i.Id, i.ContactId, i.Type, i.OccurredAt, i.Body))
            .ToListAsync(ct);

        return new PagedResponse<InteractionResponse>(items, paging.Page, paging.PageSize, total);
    }
}
