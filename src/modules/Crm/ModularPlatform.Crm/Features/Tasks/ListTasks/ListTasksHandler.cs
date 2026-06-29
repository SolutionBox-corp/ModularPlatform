using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.ListTasks;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS; due-soonest first; bounded page size.</summary>
internal sealed class ListTasksHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListTasksQuery, PagedResponse<TaskResponse>>
{
    public async Task<PagedResponse<TaskResponse>> Handle(ListTasksQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);

        var filtered = db.Tasks.Where(t => t.UserId == query.UserId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToLowerInvariant();
            filtered = filtered.Where(t => t.Status == status);
        }

        if (query.DueBefore is { } due)
        {
            filtered = filtered.Where(t => t.DueAt != null && t.DueAt <= due);
        }

        if (query.ContactId is { } contactId)
        {
            filtered = filtered.Where(t => t.ContactId == contactId);
        }

        if (query.DealId is { } dealId)
        {
            filtered = filtered.Where(t => t.DealId == dealId);
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(t => new TaskResponse(
                t.Id, t.ContactId, t.DealId, t.Title, t.Description, t.DueAt, t.Priority, t.Status,
                t.CompletedAt, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResponse<TaskResponse>(items, paging.Page, paging.PageSize, total);
    }
}
