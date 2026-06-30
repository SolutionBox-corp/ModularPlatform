using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.GetTask;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS; foreign/missing ⇒ 404 (leaks nothing).</summary>
internal sealed class GetTaskHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetTaskQuery, TaskResponse>
{
    public async Task<TaskResponse> Handle(GetTaskQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.Tasks
            .Where(t => t.Id == query.TaskId && t.UserId == query.UserId)
            .Select(t => new TaskResponse(
                t.Id, t.ContactId, t.DealId, t.AssigneeUserId, t.Title, t.Description, t.DueAt, t.Priority, t.Status,
                t.CompletedAt, t.CreatedAt, t.UpdatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("crm.task_not_found", "Task not found.");
    }
}
