using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.ListTaskComments;

internal sealed class ListTaskCommentsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListTaskCommentsQuery, PagedResponse<TaskCommentResponse>>
{
    public async Task<PagedResponse<TaskCommentResponse>> Handle(ListTaskCommentsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var taskExists = await db.Tasks
            .AnyAsync(t => t.Id == query.TaskId && t.UserId == query.UserId, ct);
        if (!taskExists)
        {
            throw new NotFoundException("crm.task_not_found", "Task not found.");
        }

        var paging = new PageRequest(query.Page, query.PageSize);
        var filtered = db.TaskComments.Where(c => c.TaskId == query.TaskId && c.UserId == query.UserId);
        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderByDescending(c => c.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new TaskCommentResponse(c.Id, c.TaskId, c.Body, c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<TaskCommentResponse>(items, paging.Page, paging.PageSize, total);
    }
}
