using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Pulls.GetPullStatus;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Features.Pulls.ListPulls;

/// <summary>Paged list of the caller's pulls, newest first. Owner-scoped by the explicit <c>WHERE UserId</c> and RLS.</summary>
internal sealed class ListPullsHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<ListPullsQuery, PagedResponse<PullStatusResponse>>
{
    public async Task<PagedResponse<PullStatusResponse>> Handle(ListPullsQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        return await db.DataPulls
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PullStatusResponse(
                p.Id, p.Source.ToString(), p.Status.ToString(), p.ErrorCode, p.CompletedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
