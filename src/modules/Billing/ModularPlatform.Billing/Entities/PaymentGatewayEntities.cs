using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Payments;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// A tenant's payment-gateway configuration for one plane (tenant-plane = end-users paying the tenant). Carries an
/// EXPLICIT <see cref="TenantId"/> (not <c>ITenantScoped</c>) because the resolver must read it from the SYSTEM Worker
/// context (webhook processing) for an ARBITRARY tenant — an automatic per-tenant filter would hide it there. The
/// actual credentials live encrypted in <see cref="TenantSecret"/> (referenced by purpose); only metadata is here.
/// </summary>
internal sealed class PaymentConfiguration : Entity
{
    public Guid TenantId { get; set; }
    public PaymentPlane Plane { get; set; }
    public PaymentProvider Provider { get; set; }
    public string Currency { get; set; } = "CZK";
    public PaymentConfigStatus Status { get; set; } = PaymentConfigStatus.PendingValidation;

    /// <summary>High-entropy token embedded in the per-tenant webhook URL (unguessable; GoPay has no signature).</summary>
    public string? WebhookToken { get; set; }

    /// <summary>GoPay merchant id (the payee account). Non-secret. Null for non-GoPay providers.</summary>
    public long? GoPayGoid { get; set; }

    /// <summary>GoPay sandbox vs production base URL selector.</summary>
    public bool Sandbox { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal enum PaymentConfigStatus
{
    PendingValidation = 0,
    Active = 1,
    Degraded = 2,
    Suspended = 3,
}

/// <summary>
/// A tenant- (or platform-) scoped secret at rest: a gateway API key / webhook secret sealed by
/// <c>ISecretProtector</c>. Stores ONLY ciphertext (+ key version / wrapped DEK) — never plaintext. Explicit
/// <see cref="TenantId"/> for the same SYSTEM-resolve reason as <see cref="PaymentConfiguration"/>; NOT
/// <c>IUserOwned</c> (a tenant secret must survive a user's GDPR erasure, unlike a per-subject DEK).
/// </summary>
internal sealed class TenantSecret : Entity
{
    public Guid TenantId { get; set; }

    /// <summary>What this secret is, e.g. <c>stripe.api_key</c> / <c>stripe.webhook_secret</c> / <c>gopay.client_secret</c>.</summary>
    public string Purpose { get; set; } = string.Empty;

    public int KeyVersion { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public byte[]? WrappedDek { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class PaymentConfigurationConfiguration : IEntityTypeConfiguration<PaymentConfiguration>
{
    public void Configure(EntityTypeBuilder<PaymentConfiguration> builder)
    {
        builder.ToTable("payment_configurations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Plane).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.Provider).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(c => c.WebhookToken).HasMaxLength(64);
        builder.HasIndex(c => new { c.TenantId, c.Plane }).IsUnique();
    }
}

internal sealed class TenantSecretConfiguration : IEntityTypeConfiguration<TenantSecret>
{
    public void Configure(EntityTypeBuilder<TenantSecret> builder)
    {
        builder.ToTable("tenant_secrets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Purpose).HasMaxLength(64).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.Purpose }).IsUnique();
    }
}
