using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// One wallet per user/tenant. <see cref="Posted"/>/<see cref="Pending"/>/<see cref="Available"/> are a
/// cached PROJECTION verified against the append-only ledger inside the pessimistic lock — never trusted
/// alone. The row is the lock target (<c>SELECT … FOR NO KEY UPDATE</c> on the debit path; xmin elsewhere).
/// </summary>
internal sealed class CreditAccount : AuditableEntity, ITenantScoped
{
    public Guid UserId { get; set; }

    /// <summary>Net posted balance = credits minus confirmed debits.</summary>
    public long Posted { get; set; }

    /// <summary>Sum of active (non-expired) holds.</summary>
    public long Pending { get; set; }

    /// <summary>Projection of <c>Posted - active holds</c>; must never be negative.</summary>
    public long Available { get; set; }
}

internal sealed class CreditAccountConfiguration : IEntityTypeConfiguration<CreditAccount>
{
    public void Configure(EntityTypeBuilder<CreditAccount> builder)
    {
        builder.ToTable("credit_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Posted).IsRequired();
        builder.Property(a => a.Pending).IsRequired();
        builder.Property(a => a.Available).IsRequired();
        builder.HasIndex(a => a.UserId).IsUnique();
    }
}
