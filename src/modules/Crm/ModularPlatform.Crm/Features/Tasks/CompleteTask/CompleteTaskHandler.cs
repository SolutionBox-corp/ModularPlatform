using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.CompleteTask;

/// <summary>
/// Marks the caller's own task done. Idempotent: an already-done task is a no-op (a double-click or retry sets no
/// second CompletedAt). Foreign/missing ⇒ 404.
/// </summary>
internal sealed class CompleteTaskHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<CompleteTaskCommand, Unit>
{
    public async Task<Unit> Handle(CompleteTaskCommand command, CancellationToken ct)
    {
        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId && t.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.task_not_found", "Task not found.");

        if (task.Status == TaskStatuses.Done)
        {
            return Unit.Value;
        }

        task.Status = TaskStatuses.Done;
        task.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
