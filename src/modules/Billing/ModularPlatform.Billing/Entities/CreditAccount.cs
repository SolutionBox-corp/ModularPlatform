using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// One wallet per user/tenant. <see cref="Posted"/>/<see cref="Pending"/>/<see cref="Available"/> are an
/// authoritative PROJECTION maintained transactionally (invariant <c>Available = Posted − Pending</c>). The DEBIT
/// path is an atomic conditional <c>ExecuteUpdate</c> guard (<c>WHERE Available &gt;= amount</c>) — the EF-native
/// pessimistic equivalent that locks the row and evaluates the guard in one statement, so concurrent reservations
/// serialize at the DB with no double-spend; every other mutation uses xmin + the retry behavior.
/// </summary>
internal sealed class CreditAccount : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }

    /// <summary>
    /// The tenant the wallet belongs to (two-plane model + per-tenant reporting). Stamped EXPLICITLY by the
    /// provisioning handler from the registration event — the Worker runs in the SYSTEM context, so the
    /// tenant-stamping interceptor does NOT fill it. Nullable for accounts provisioned before this column existed.
    /// </summary>
    public Guid? TenantId { get; set; }

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
        builder.ToTable("credit_accounts", t =>
        {
            // Defence-in-depth at the DB level: the balance projection must never go negative even if a
            // handler bug or a manual write tried to. Postgres rejects the write; the application-level
            // atomic ExecuteUpdate guard (WHERE available >= amount) is the primary protection.
            t.HasCheckConstraint("ck_credit_accounts_posted_non_negative", "\"Posted\" >= 0");
            t.HasCheckConstraint("ck_credit_accounts_pending_non_negative", "\"Pending\" >= 0");
            t.HasCheckConstraint("ck_credit_accounts_available_non_negative", "\"Available\" >= 0");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Posted).IsRequired();
        builder.Property(a => a.Pending).IsRequired();
        builder.Property(a => a.Available).IsRequired();
        builder.HasIndex(a => a.UserId).IsUnique();
        // Per-tenant reporting/reconciliation queries filter by TenantId — index it (the prior schema had this index;
        // it was dropped together with the column and reinstated without it, so this restores the lookup path).
        builder.HasIndex(a => a.TenantId);
    }
}
