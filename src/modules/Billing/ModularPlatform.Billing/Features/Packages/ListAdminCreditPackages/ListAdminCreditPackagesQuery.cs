using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Packages.ListAdminCreditPackages;

/// <summary>
/// Admin read of the FULL package catalogue the caller may manage — INCLUDING inactive ones (the public
/// <c>ListCreditPackages</c> returns only purchasable/active rows). Scope is the caller's own tenant + the
/// platform-global rows; for the SYSTEM platform admin (no tenant) that resolves to the global catalogue.
/// </summary>
public sealed record ListAdminCreditPackagesQuery(PageRequest Page) : IQuery<PagedResponse<AdminCreditPackageResponse>>;

public sealed record AdminCreditPackageResponse(
    Guid Id,
    string Name,
    long CreditAmount,
    decimal Price,
    string Currency,
    int? BucketExpiryDays,
    bool Active,
    string? StripePriceId);
