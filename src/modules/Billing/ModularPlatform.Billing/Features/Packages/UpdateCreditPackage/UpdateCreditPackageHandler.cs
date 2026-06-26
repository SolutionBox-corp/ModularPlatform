using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.UpdateCreditPackage;

/// <summary>
/// Admin catalogue update (gated by <c>billing.manage</c>). Tracked save — xmin + audit apply. In-flight
/// purchases are unaffected: the saga snapshot carries the amounts from purchase time.
/// </summary>
internal sealed class UpdateCreditPackageHandler(BillingDbContext db, ITenantContext tenant)
    : ICommandHandler<UpdateCreditPackageCommand, UpdateCreditPackageResponse>
{
    public async Task<UpdateCreditPackageResponse> Handle(UpdateCreditPackageCommand command, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var name = command.Name.Trim();

        var package = await db.CreditPackages
            .FirstOrDefaultAsync(p => p.Id == command.PackageId && p.TenantId == tenantId, ct)
            ?? throw new NotFoundException("billing.package_not_found", "Credit package not found.");

        if (await db.CreditPackages.AnyAsync(
                p => p.Id != command.PackageId && p.TenantId == tenantId && p.Name == name, ct))
        {
            throw new ConflictException("billing.package.name_taken", "A package with this name already exists.");
        }

        package.Name = name;
        package.CreditAmount = command.CreditAmount;
        package.Price = command.Price;
        package.BucketExpiryDays = command.BucketExpiryDays;
        package.Active = command.Active;
        package.StripePriceId = command.StripePriceId;

        await db.SaveChangesAsync(ct);

        return new UpdateCreditPackageResponse(package.Id, package.Active);
    }
}
