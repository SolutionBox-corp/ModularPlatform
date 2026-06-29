using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.GetCreditLedger;

public sealed record GetCreditLedgerQuery(Guid UserId, PageRequest Page)
    : IQuery<PagedResponse<CreditLedgerEntry>>;

/// <summary>
/// A single append-only ledger row exposed to the owner. <c>Direction</c> is Credit/Debit, <c>Type</c> is the
/// business reason (Topup/Spend/Reservation/Release/Expiry/Adjustment/Refund), <c>IdempotencyKey</c> is the
/// operation reference, <c>TransactionId</c> groups a balanced set.
/// </summary>
public sealed record CreditLedgerEntry(
    Guid Id,
    string Direction,
    long Amount,
    string Type,
    Guid TransactionId,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);
