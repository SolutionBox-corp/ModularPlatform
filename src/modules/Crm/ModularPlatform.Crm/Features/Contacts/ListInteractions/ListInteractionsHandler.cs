using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.ListInteractions;

/// <summary>Read slice. Owner-scoped by WHERE + RLS; a foreign contact id yields an empty list (leaks nothing).</summary>
internal sealed class ListInteractionsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListInteractionsQuery, IReadOnlyList<InteractionResponse>>
{
    public async Task<IReadOnlyList<InteractionResponse>> Handle(ListInteractionsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var limit = Math.Clamp(query.Limit, 1, 500);

        return await db.ContactInteractions
            .Where(i => i.ContactId == query.ContactId && i.UserId == query.UserId)
            .OrderByDescending(i => i.OccurredAt)
            .Take(limit)
            .Select(i => new InteractionResponse(i.Id, i.ContactId, i.Type, i.OccurredAt, i.Body))
            .ToListAsync(ct);
    }
}
