using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.ListCreditPackages;

public sealed record ListCreditPackagesQuery : IQuery<IReadOnlyList<CreditPackageResponse>>;

public sealed record CreditPackageResponse(
    Guid Id,
    string Name,
    long CreditAmount,
    decimal Price,
    string Currency,
    int? BucketExpiryDays);
