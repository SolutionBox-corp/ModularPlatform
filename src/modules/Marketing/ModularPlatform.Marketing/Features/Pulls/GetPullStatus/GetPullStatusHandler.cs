using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Pulls.GetPullStatus;

/// <summary>
/// Reads a pull's status for the caller who owns it. Owner-scoped both by the explicit <c>UserId</c> predicate (from
/// the token) and by RLS — a foreign id is a 404 even with <c>Persistence:Rls:Enabled=false</c>.
/// </summary>
internal sealed class GetPullStatusHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<GetPullStatusQuery, PullStatusResponse>
{
    public async Task<PullStatusResponse> Handle(GetPullStatusQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var pull = await db.DataPulls
            .Where(p => p.Id == query.DataPullId && p.UserId == query.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("marketing.pull_not_found", "Pull not found.");

        return new PullStatusResponse(
            pull.Id, pull.Source.ToString(), pull.Status.ToString(), pull.ErrorCode, pull.CompletedAt);
    }
}
