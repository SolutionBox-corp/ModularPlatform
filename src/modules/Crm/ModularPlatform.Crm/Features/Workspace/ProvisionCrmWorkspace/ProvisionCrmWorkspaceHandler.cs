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

        var task = new CrmTask
        {
            UserId = command.UserId,
            Title = "Add your first contact",
            Description = "Welcome to CRM — add a contact, log a meeting, and track your first deal.",
            DueAt = clock.UtcNow.AddDays(1),
            Priority = TaskPriorities.Normal,
        };
        db.Tasks.Add(task);

        // CrmTask is ITenantScoped (shadow TenantId, no CLR property). The Worker runs under the SYSTEM tenant
        // context (TenantId == null), so TenantStampingInterceptor does NOT auto-stamp — a tenant-less row would be
        // hidden from the user by the tenant query filter (or RLS-rejected). Stamp the shadow column explicitly from
        // the integration event's tenant, mirroring Billing's EnsureCreditAccount.
        db.Entry(task).Property("TenantId").CurrentValue = command.TenantId;

        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
