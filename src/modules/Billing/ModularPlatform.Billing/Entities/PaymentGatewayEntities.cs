using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Payments;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// A tenant's payment-gateway configuration for one plane (tenant-plane = end-users paying the tenant). Carries an
/// EXPLICIT non-nullable <see cref="TenantId"/> and is deliberately NOT <c>ITenantScoped</c>: that convention manages a
/// SHADOW <c>Guid?</c> TenantId + an ambient query filter, but the resolver runs in the SYSTEM Worker context
/// (processing an inbound webhook for an ARBITRARY tenant) where the ambient filter is bypassed — an automatic filter
/// would hide the very row the webhook must find, and a shadow nullable key can't carry the required non-null id. So
/// the config store filters by the EXPLICIT tenant id with <c>IgnoreQueryFilters()</c>, never the ambient tenant.
/// (Defence-in-depth here is the explicit-WHERE in every path + the credentials being encrypted in
/// <see cref="TenantSecret"/>.) Only metadata is here.
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
/// <c>ISecretProtector</c>. Stores ONLY ciphertext (+ key version / wrapped DEK) — never plaintext.
/// Explicit non-nullable <see cref="TenantId"/> (NOT <c>ITenantScoped</c>) for the same SYSTEM-resolve reason as
/// <see cref="PaymentConfiguration"/>; NOT <c>IUserOwned</c> (a tenant secret must survive a user's GDPR erasure,
/// unlike a per-subject DEK).
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
