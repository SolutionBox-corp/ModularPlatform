using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.CreateCreditPackage;

/// <summary>Admin catalogue write (gated by <c>billing.manage</c>). The package belongs to the caller's tenant (B2B:
/// the tenant offers its OWN catalogue). Audited like every tracked save.</summary>
internal sealed class CreateCreditPackageHandler(BillingDbContext db, ITenantContext tenant)
    : ICommandHandler<CreateCreditPackageCommand, CreateCreditPackageResponse>
{
    public async Task<CreateCreditPackageResponse> Handle(CreateCreditPackageCommand command, CancellationToken ct)
    {
        var package = new CreditPackage
        {
            TenantId = tenant.TenantId, // the caller's tenant owns this package (null only in a system context)
            Name = command.Name,
            CreditAmount = command.CreditAmount,
            Price = command.Price,
            BucketExpiryDays = command.BucketExpiryDays,
            Active = command.Active,
            StripePriceId = command.StripePriceId,
        };

        db.CreditPackages.Add(package);
        await db.SaveChangesAsync(ct);

        return new CreateCreditPackageResponse(package.Id);
    }
}
