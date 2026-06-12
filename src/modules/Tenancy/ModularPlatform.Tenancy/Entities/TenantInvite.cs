using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Tenancy.Entities;

/// <summary>
/// A single-use invitation to JOIN a tenant whose <see cref="Tenant.RegistrationMode"/> is
/// <see cref="TenantRegistrationMode.InviteOnly"/>. Only the SHA-256 hash of the raw token is stored (the raw token
/// is shown once to the admin who created it, like a refresh token); registration presents the raw token, the gate
/// re-hashes and looks it up, then stamps <see cref="ConsumedAt"/> so it can never be reused. NOT
/// <c>ITenantScoped</c> — it is consumed in the ANONYMOUS registration context (no tenant claim), filtered by the
/// explicit <see cref="TenantId"/>.
/// </summary>
internal sealed class TenantInvite : Entity
{
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class TenantInviteConfiguration : IEntityTypeConfiguration<TenantInvite>
{
    public void Configure(EntityTypeBuilder<TenantInvite> builder)
    {
        builder.ToTable("tenant_invites");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.TenantId);
    }
}
