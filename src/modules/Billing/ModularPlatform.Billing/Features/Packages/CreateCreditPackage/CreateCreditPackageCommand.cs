using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.CreateCreditPackage;

public sealed record CreateCreditPackageCommand(
    string Name,
    long CreditAmount,
    decimal Price,
    int? BucketExpiryDays,
    bool Active,
    string? StripePriceId) : ICommand<CreateCreditPackageResponse>;

public sealed record CreateCreditPackageResponse(Guid Id);

public sealed record CreateCreditPackageRequest(
    string Name,
    long CreditAmount,
    decimal Price,
    int? BucketExpiryDays,
    bool Active,
    string? StripePriceId);
