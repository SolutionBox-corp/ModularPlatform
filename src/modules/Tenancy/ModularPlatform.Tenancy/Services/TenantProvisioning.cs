using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;
using ModularPlatform.Tenancy.Contracts;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Tenancy.Services;

/// <summary>
/// <see cref="ITenantProvisioning"/> — creates a registry row and publishes <see cref="TenantProvisionedIntegrationEvent"/>
/// in one transaction (outbox). Consumed by registration (interim auto-provision until subdomain-join is wired) and by
/// the platform-admin provisioning flow. A null subdomain is auto-generated and unique; a clashing one is a 409.
/// </summary>
internal sealed class TenantProvisioning(IDbContextOutbox<TenancyDbContext> outbox, IClock clock) : ITenantProvisioning
{
    /// <summary>
    /// Module keys a fresh tenant is entitled to by default (the product-facing modules; Identity/Tenancy are
    /// infrastructure, always on, not gated). The platform-admin can later toggle these per tenant. The keys must
    /// match the <c>.RequireModule("…")</c> keys on each module's endpoints.
    /// </summary>
    private static readonly string[] DefaultEntitledModules =
        ["billing", "notifications", "files", "operations", "gdpr"];

    public async Task<Guid> CreateAsync(string name, string? subdomain = null, CancellationToken ct = default)
    {
        var db = outbox.DbContext;

        var tenant = new Tenant
        {
            Name = name,
            Status = TenantStatus.Active,
            Placement = "shared",
            CreatedAt = clock.UtcNow,
        };
        tenant.Subdomain = string.IsNullOrWhiteSpace(subdomain)
            ? $"t-{tenant.Id:N}"
            : subdomain.Trim().ToLowerInvariant();

        db.Tenants.Add(tenant);

        foreach (var moduleKey in DefaultEntitledModules)
        {
            db.TenantEntitlements.Add(new TenantEntitlement
            {
                TenantId = tenant.Id,
                ModuleKey = moduleKey,
                Enabled = true,
                ValidFrom = clock.UtcNow,
            });
        }

        await outbox.PublishAsync(new TenantProvisionedIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: clock.UtcNow,
            TenantId: tenant.Id,
            Subdomain: tenant.Subdomain,
            Name: tenant.Name));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("tenant.subdomain_taken", "This subdomain is already taken.");
        }

        return tenant.Id;
    }

    public async Task DeleteAsync(Guid tenantId, CancellationToken ct = default)
    {
        var db = outbox.DbContext;

        var entitlements = await db.TenantEntitlements.Where(e => e.TenantId == tenantId).ToListAsync(ct);
        if (entitlements.Count > 0)
        {
            db.TenantEntitlements.RemoveRange(entitlements);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is not null)
        {
            db.Tenants.Remove(tenant);
        }

        await db.SaveChangesAsync(ct);
    }
}
