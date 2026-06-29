using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.DeleteTask;

/// <summary>Soft-deletes a tracked task owned by the caller. Foreign/missing ⇒ 404. No event is published.</summary>
internal sealed class DeleteTaskHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<DeleteTaskCommand, Unit>
{
    public async Task<Unit> Handle(DeleteTaskCommand command, CancellationToken ct)
    {
        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId && t.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.task_not_found", "Task not found.");

        task.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
