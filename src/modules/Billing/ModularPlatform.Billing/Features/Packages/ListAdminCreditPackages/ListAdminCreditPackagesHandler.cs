using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Packages.ListAdminCreditPackages;

/// <summary>
/// Admin catalogue read: ALL packages (active + inactive) the caller may manage — its OWN tenant's rows plus the
/// platform-global rows. The SYSTEM platform admin has no tenant, so it sees the global catalogue (TenantId null).
/// </summary>
internal sealed class ListAdminCreditPackagesHandler(
    IReadDbContextFactory<BillingDbContext> readFactory, ITenantContext tenant)
    : IQueryHandler<ListAdminCreditPackagesQuery, PagedResponse<AdminCreditPackageResponse>>
{
    public async Task<PagedResponse<AdminCreditPackageResponse>> Handle(
        ListAdminCreditPackagesQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();
        var tenantId = tenant.TenantId;

        return await db.CreditPackages
            .Where(p => p.TenantId == tenantId || p.TenantId == null)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Select(p => new AdminCreditPackageResponse(
                p.Id, p.Name, p.CreditAmount, p.Price, p.Currency, p.BucketExpiryDays, p.Active, p.StripePriceId))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
