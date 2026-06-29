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

        // Reserved-label guard at the SERVICE layer (not only in the admin validator) — any port caller that passes a
        // subdomain (a future module/saga/job) must not be able to provision a control-plane label.
        if (ReservedSubdomains.All.Contains(tenant.Subdomain))
        {
            throw new ConflictException("tenant.subdomain.reserved", "This subdomain is reserved.");
        }

        db.Tenants.Add(tenant);

        // Fresh tenants are entitled to the default product modules (CRM is default-on so self-serve registration
        // lands on a working CRM); an admin can later disable any key per tenant.
        foreach (var moduleKey in ProductModuleKeys.DefaultEntitled)
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
