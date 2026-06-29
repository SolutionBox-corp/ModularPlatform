using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Contracts;

public sealed record GetCreditBalanceQuery(Guid UserId) : IQuery<CreditBalanceResponse>;

public sealed record CreditBalanceResponse(Guid AccountId, Guid UserId, long Posted, long Pending, long Available);

public sealed record ReserveCreditsCommand(Guid UserId, long Amount, int? HoldMinutes = null)
    : ICommand<ReserveCreditsResponse>;

public sealed record ReserveCreditsResponse(Guid ReservationId, long Available);

public sealed record ConfirmSpendCommand(Guid UserId, Guid ReservationId) : ICommand<ConfirmSpendResponse>;

public sealed record ConfirmSpendResponse(Guid AccountId, long Posted, long Available);

public sealed record ReleaseHoldCommand(Guid UserId, Guid ReservationId) : ICommand<ReleaseHoldResponse>;

public sealed record ReleaseHoldResponse(Guid AccountId, long Available);
