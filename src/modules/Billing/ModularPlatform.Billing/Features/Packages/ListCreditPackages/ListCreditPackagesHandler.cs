using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Packages.ListCreditPackages;

/// <summary>Read slice: the purchasable catalogue (active packages only), cheapest first.</summary>
internal sealed class ListCreditPackagesHandler(IReadDbContextFactory<BillingDbContext> readFactory)
    : IQueryHandler<ListCreditPackagesQuery, IReadOnlyList<CreditPackageResponse>>
{
    public async Task<IReadOnlyList<CreditPackageResponse>> Handle(ListCreditPackagesQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.CreditPackages
            .Where(p => p.Active)
            .OrderBy(p => p.Price)
            .Select(p => new CreditPackageResponse(p.Id, p.Name, p.CreditAmount, p.Price, p.BucketExpiryDays))
            .ToListAsync(ct);
    }
}
