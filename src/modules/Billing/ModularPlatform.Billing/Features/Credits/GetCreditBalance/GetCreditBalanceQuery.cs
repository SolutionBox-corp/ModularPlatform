using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.GetCreditBalance;

public sealed record GetCreditBalanceQuery(Guid UserId) : IQuery<CreditBalanceResponse>;

public sealed record CreditBalanceResponse(Guid AccountId, Guid UserId, long Posted, long Pending, long Available);
