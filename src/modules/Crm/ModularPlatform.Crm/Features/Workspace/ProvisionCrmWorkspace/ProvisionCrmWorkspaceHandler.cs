using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Workspace.ProvisionCrmWorkspace;

/// <summary>
/// Provisions a starter task for a new user. Idempotent: if the user already has a task, no-op (so a redelivered
/// UserRegistered or a re-run after an xmin conflict can't seed twice). Scoped write context; no event published.
/// </summary>
internal sealed class ProvisionCrmWorkspaceHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<ProvisionCrmWorkspaceCommand, Unit>
{
    public async Task<Unit> Handle(ProvisionCrmWorkspaceCommand command, CancellationToken ct)
    {
        var already = await db.Tasks.IgnoreQueryFilters().AnyAsync(t => t.UserId == command.UserId, ct);
        if (already)
        {
            return Unit.Value;
        }

        db.Tasks.Add(new CrmTask
        {
            UserId = command.UserId,
            Title = "Add your first contact",
            Description = "Welcome to CRM — add a contact, log a meeting, and track your first deal.",
            DueAt = clock.UtcNow.AddDays(1),
            Priority = TaskPriorities.Normal,
        });

        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
