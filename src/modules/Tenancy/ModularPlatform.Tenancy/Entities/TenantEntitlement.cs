using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Tenancy.Entities;

/// <summary>
/// One tenant's grant for one module: the INNER bound of "is this module available to THIS tenant". The OUTER bound is
/// the deployment flag <c>Modules:{Name}:Enabled</c> (is the code even loaded). Enforcement is the
/// <c>ModuleEntitlementGuard</c> + <c>.RequireModule(key)</c> (404 when not entitled). Not <see cref="ITenantScoped"/>:
/// it carries an EXPLICIT <see cref="TenantId"/> and is read by the resolver filtered to that id (and cross-tenant in
/// the SYSTEM platform-admin plane), so an automatic per-tenant filter would get in the way.
/// </summary>
internal sealed class TenantEntitlement : Entity
{
    public Guid TenantId { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Tier { get; set; }

    /// <summary>Free-form per-module limits (JSONB), e.g. <c>{ "maxUsers": 50 }</c>. Null = no explicit limits.</summary>
    public string? Limits { get; set; }

    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}

internal sealed class TenantEntitlementConfiguration : IEntityTypeConfiguration<TenantEntitlement>
{
    public void Configure(EntityTypeBuilder<TenantEntitlement> builder)
    {
        builder.ToTable("tenant_entitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ModuleKey).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Tier).HasMaxLength(64);
        builder.Property(e => e.Limits).HasColumnType("jsonb");
        builder.HasIndex(e => new { e.TenantId, e.ModuleKey }).IsUnique();
    }
}
