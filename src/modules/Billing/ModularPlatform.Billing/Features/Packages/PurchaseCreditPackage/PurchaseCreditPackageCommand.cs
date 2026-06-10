using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.PurchaseCreditPackage;

public sealed record PurchaseCreditPackageCommand(Guid UserId, Guid PackageId)
    : ICommand<PurchaseCreditPackageResponse>;

public sealed record PurchaseCreditPackageResponse(Guid PurchaseId, string CheckoutSessionId, string CheckoutUrl);
