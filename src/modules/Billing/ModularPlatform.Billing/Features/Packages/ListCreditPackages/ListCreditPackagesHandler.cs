using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Packages.ListCreditPackages;

/// <summary>Read slice: the purchasable catalogue (active packages only), cheapest first. The caller sees its OWN
/// tenant's packages plus any platform-global ones (B2B per-tenant catalogue).</summary>
internal sealed class ListCreditPackagesHandler(
    IReadDbContextFactory<BillingDbContext> readFactory, ITenantContext tenant)
    : IQueryHandler<ListCreditPackagesQuery, IReadOnlyList<CreditPackageResponse>>
{
    public async Task<IReadOnlyList<CreditPackageResponse>> Handle(ListCreditPackagesQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();
        var tenantId = tenant.TenantId;

        return await db.CreditPackages
            .Where(p => p.Active && (p.TenantId == tenantId || p.TenantId == null))
            .OrderBy(p => p.Price)
            .Select(p => new CreditPackageResponse(p.Id, p.Name, p.CreditAmount, p.Price, p.Currency, p.BucketExpiryDays))
            .ToListAsync(ct);
    }
}
