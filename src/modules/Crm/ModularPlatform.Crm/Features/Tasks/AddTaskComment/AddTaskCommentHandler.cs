using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.AddTaskComment;

internal sealed class AddTaskCommentHandler(CrmDbContext db)
    : ICommandHandler<AddTaskCommentCommand, AddTaskCommentResponse>
{
    public async Task<AddTaskCommentResponse> Handle(AddTaskCommentCommand command, CancellationToken ct)
    {
        var taskExists = await db.Tasks
            .AnyAsync(t => t.Id == command.TaskId && t.UserId == command.UserId, ct);
        if (!taskExists)
        {
            throw new NotFoundException("crm.task_not_found", "Task not found.");
        }

        var comment = new CrmTaskComment
        {
            UserId = command.UserId,
            TaskId = command.TaskId,
            Body = command.Body.Trim(),
        };

        db.TaskComments.Add(comment);
        await db.SaveChangesAsync(ct);

        return new AddTaskCommentResponse(comment.Id);
    }
}
