using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Persistence;

/// <summary>
/// Billing module's DbContext. Entity configs are discovered from this assembly; xmin concurrency,
/// tenant filter and the per-module audit table are applied by the base. The ledger tables are
/// append-only at the application level (entries/holds are never UPDATE/DELETEd except hold status).
/// The TYPE is public ONLY because Wolverine's generated saga handlers (external codegen assembly) must
/// resolve it for EF saga persistence — same precedent as public handler shells. The DbSets stay internal:
/// no other module can touch Billing data (entities are internal; ArchUnitNET enforces no Core references).
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "billing";

    internal DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();
    internal DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();
    internal DbSet<CreditBucket> CreditBuckets => Set<CreditBucket>();
    internal DbSet<CreditHold> CreditHolds => Set<CreditHold>();
    internal DbSet<StripeEvent> StripeEvents => Set<StripeEvent>();
    internal DbSet<CreditPackage> CreditPackages => Set<CreditPackage>();
    internal DbSet<Subscription> Subscriptions => Set<Subscription>();

    // Per-tenant payment gateway config + encrypted credentials (tenant-plane: end-users paying the tenant).
    internal DbSet<PaymentConfiguration> PaymentConfigurations => Set<PaymentConfiguration>();
    internal DbSet<TenantSecret> TenantSecrets => Set<TenantSecret>();

    /// <summary>Wolverine saga state — doubles as the user-facing purchase record (never MarkCompleted).</summary>
    internal DbSet<CreditPurchaseSaga> CreditPurchaseSagas => Set<CreditPurchaseSaga>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureCreditEntriesAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureCreditEntriesAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureCreditEntriesAreAppendOnly()
    {
        var mutatedLedgerEntry = ChangeTracker.Entries<CreditEntry>()
            .FirstOrDefault(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (mutatedLedgerEntry is not null)
        {
            throw new InvalidOperationException(
                "credit_entries is append-only. Add a compensating CreditEntry instead of updating or deleting a ledger row.");
        }
    }
}
