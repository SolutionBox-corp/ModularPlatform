using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.CreateCreditPackage;

/// <summary>Admin catalogue write (gated by <c>billing.manage</c>). Audited like every tracked save.</summary>
internal sealed class CreateCreditPackageHandler(BillingDbContext db)
    : ICommandHandler<CreateCreditPackageCommand, CreateCreditPackageResponse>
{
    public async Task<CreateCreditPackageResponse> Handle(CreateCreditPackageCommand command, CancellationToken ct)
    {
        var package = new CreditPackage
        {
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
