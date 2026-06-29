using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.UpdateTask;

/// <summary>Loads the caller's OWN tracked task (foreign/deleted ⇒ 404) and applies a PARTIAL patch (null = unchanged).</summary>
internal sealed class UpdateTaskHandler(CrmDbContext db)
    : ICommandHandler<UpdateTaskCommand, TaskResponse>
{
    public async Task<TaskResponse> Handle(UpdateTaskCommand command, CancellationToken ct)
    {
        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId && t.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.task_not_found", "Task not found.");

        if (command.Title is not null)
        {
            task.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            task.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description;
        }

        if (command.DueAt is not null)
        {
            task.DueAt = command.DueAt;
        }

        if (command.Priority is not null)
        {
            task.Priority = command.Priority;
        }

        await db.SaveChangesAsync(ct);

        return new TaskResponse(
            task.Id, task.ContactId, task.DealId, task.Title, task.Description, task.DueAt, task.Priority,
            task.Status, task.CompletedAt, task.CreatedAt, task.UpdatedAt);
    }
}
