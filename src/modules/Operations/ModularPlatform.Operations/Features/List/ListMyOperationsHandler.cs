using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Operations.Features.List;

/// <summary>
/// Paged list of the caller's own operations (newest first). No-tracking read factory. Owner-scoped both by the
/// explicit <c>WHERE UserId</c> (from the token) and by RLS (defence in depth). The <c>Status</c> enum is stored as
/// a string column, so it is counted/ordered in the DB and rendered to its string form after materialization.
/// </summary>
internal sealed class ListMyOperationsHandler(IReadDbContextFactory<OperationsDbContext> readDb)
    : IQueryHandler<ListMyOperationsQuery, PagedResponse<OperationListItem>>
{
    public async Task<PagedResponse<OperationListItem>> Handle(ListMyOperationsQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var ordered = db.Operations
            .Where(o => o.UserId == query.UserId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id);

        var totalCount = await ordered.LongCountAsync(ct);

        var rows = await ordered
            .Skip(query.Page.Skip)
            .Take(query.Page.PageSize)
            .Select(o => new { o.Id, o.Type, o.Status, o.ErrorCode, o.CompletedAt, o.CreatedAt })
            .ToListAsync(ct);

        var items = rows
            .Select(o => new OperationListItem(
                o.Id, o.Type, o.Status.ToString(), o.ErrorCode, o.CompletedAt, o.CreatedAt))
            .ToList();

        return new PagedResponse<OperationListItem>(items, query.Page.Page, query.Page.PageSize, totalCount);
    }
}
