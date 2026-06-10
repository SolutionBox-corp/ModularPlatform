using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.UpdateCreditPackage;

public sealed record UpdateCreditPackageCommand(
    Guid PackageId,
    string Name,
    long CreditAmount,
    decimal Price,
    int? BucketExpiryDays,
    bool Active,
    string? StripePriceId) : ICommand<UpdateCreditPackageResponse>;

public sealed record UpdateCreditPackageResponse(Guid Id, bool Active);

public sealed record UpdateCreditPackageRequest(
    string Name,
    long CreditAmount,
    decimal Price,
    int? BucketExpiryDays,
    bool Active,
    string? StripePriceId);
