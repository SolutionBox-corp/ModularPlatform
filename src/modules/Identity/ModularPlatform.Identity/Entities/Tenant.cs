using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A tenant (customer organization). The tenant ROOT — it is NOT <see cref="ITenantScoped"/> itself; every
/// tenant-scoped row carries this tenant's Id in its shadow <c>TenantId</c>. Created during registration.
/// </summary>
internal sealed class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(256).IsRequired();
    }
}
