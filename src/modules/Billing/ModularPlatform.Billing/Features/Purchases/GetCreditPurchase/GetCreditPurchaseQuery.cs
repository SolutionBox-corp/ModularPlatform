using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Purchases.GetCreditPurchase;

public sealed record GetCreditPurchaseQuery(Guid UserId, Guid PurchaseId) : IQuery<CreditPurchaseResponse>;

public sealed record CreditPurchaseResponse(
    Guid PurchaseId,
    Guid PackageId,
    long CreditAmount,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt);
