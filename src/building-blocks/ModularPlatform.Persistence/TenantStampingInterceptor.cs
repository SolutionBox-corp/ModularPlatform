using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Persistence;

/// <summary>
/// Stamps the shadow <c>TenantId</c> onto every newly-inserted <see cref="ITenantScoped"/> entity from the current
/// request's tenant claim. Without this, tenant-scoped rows persist with <c>TenantId = NULL</c> and the tenant query
/// filter never matches them. A handler that creates a row in an anonymous/cross-tenant context (e.g. registration
/// creating the tenant + its first user) sets <c>TenantId</c> explicitly; this interceptor only fills the gap when
/// it is unset AND a tenant is in context, so it never overwrites an explicit assignment.
/// </summary>
public sealed class TenantStampingInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null || tenant.TenantId is not { } tenantId)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry is { State: EntityState.Added, Entity: ITenantScoped })
            {
                var tenantProperty = entry.Property("TenantId");
                if (tenantProperty.CurrentValue is null)
                {
                    tenantProperty.CurrentValue = tenantId;
                }
            }
        }
    }
}
