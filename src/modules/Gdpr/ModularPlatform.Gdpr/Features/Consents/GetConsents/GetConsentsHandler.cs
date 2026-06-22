using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Gdpr.Features.Consents.GetConsents;

/// <summary>
/// Read slice. Returns the subject's full append-only consent history (newest first) via the
/// no-tracking read factory. Queries never mutate, never publish, never open a transaction.
/// </summary>
internal sealed class GetConsentsHandler(IReadDbContextFactory<GdprDbContext> readFactory)
    : IQueryHandler<GetConsentsQuery, IReadOnlyList<ConsentResponse>>
{
    public async Task<IReadOnlyList<ConsentResponse>> Handle(GetConsentsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        // Cap the append-only history — a self-service grant/withdraw loop could otherwise amplify an unbounded heap
        // read. The newest 500 records are far more than any real consent timeline needs.
        return await db.ConsentRecords
            .Where(c => c.UserId == query.UserId)
            .OrderByDescending(c => c.RecordedAt)
            .Take(500)
            .Select(c => new ConsentResponse(c.Id, c.ConsentType, c.Granted, c.RecordedAt, c.PolicyVersion))
            .ToListAsync(ct);
    }
}
