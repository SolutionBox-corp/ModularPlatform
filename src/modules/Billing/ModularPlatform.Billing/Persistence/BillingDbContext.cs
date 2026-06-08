using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Persistence;

/// <summary>
/// Billing module's DbContext. Entity configs are discovered from this assembly; xmin concurrency,
/// tenant filter and the per-module audit table are applied by the base. The ledger tables are
/// append-only at the application level (entries/holds are never UPDATE/DELETEd except hold status).
/// </summary>
internal sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "billing";

    public DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();
    public DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();
    public DbSet<CreditBucket> CreditBuckets => Set<CreditBucket>();
    public DbSet<CreditHold> CreditHolds => Set<CreditHold>();
    public DbSet<StripeEvent> StripeEvents => Set<StripeEvent>();
    public DbSet<CreditPackage> CreditPackages => Set<CreditPackage>();
}
