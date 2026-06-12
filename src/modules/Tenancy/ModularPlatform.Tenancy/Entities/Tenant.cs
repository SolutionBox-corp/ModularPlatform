using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Tenancy.Entities;

/// <summary>
/// A tenant (customer organization) — the platform-root registry row. It is NOT <see cref="ITenantScoped"/> itself;
/// every tenant-scoped row across modules carries this tenant's Id in its shadow <c>TenantId</c>. Read in the SYSTEM
/// plane (platform-admin) and pre-auth (subdomain resolution), so it is deliberately outside the tenant query filter.
/// <para>
/// <see cref="Subdomain"/> is the routing key (unique). <see cref="Placement"/> is the pool→silo seam: <c>shared</c>
/// today (the global connection), <c>dedicated:&lt;key&gt;</c> later for a per-tenant DB — a data flip, no redesign.
/// </para>
/// </summary>
internal sealed class Tenant : Entity
{
    public string Subdomain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>
    /// Who may JOIN this workspace via its subdomain. Defaults to <see cref="TenantRegistrationMode.InviteOnly"/>
    /// (secure by default) — an anonymous signup on a tenant subdomain is rejected unless it carries a valid
    /// single-use invite. The self-serve CREATOR (apex signup that provisions a fresh tenant) is unaffected: that
    /// path doesn't "join", it creates the tenant + its first member in one step.
    /// </summary>
    public TenantRegistrationMode RegistrationMode { get; set; } = TenantRegistrationMode.InviteOnly;

    public string Placement { get; set; } = "shared";
    public string? DbDsnSecretRef { get; set; }
    public int InfraRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal enum TenantStatus
{
    Provisioning = 0,
    Active = 1,
    Suspended = 2,
    Separating = 3,
    Dedicated = 4,
}

/// <summary>How a new user may join an existing tenant via its subdomain.</summary>
internal enum TenantRegistrationMode
{
    /// <summary>Anyone may self-register into the workspace (no invite needed).</summary>
    Open = 0,

    /// <summary>A valid single-use invite token is required to join (default — secure).</summary>
    InviteOnly = 1,

    /// <summary>No new members may join (registration is closed).</summary>
    Closed = 2,
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Subdomain).HasMaxLength(63).IsRequired();
        builder.HasIndex(t => t.Subdomain).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.RegistrationMode).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Placement).HasMaxLength(128).IsRequired();
        builder.Property(t => t.DbDsnSecretRef).HasMaxLength(256);
    }
}
