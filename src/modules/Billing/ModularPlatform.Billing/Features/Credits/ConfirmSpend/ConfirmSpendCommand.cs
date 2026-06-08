using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ConfirmSpend;

public sealed record ConfirmSpendCommand(Guid UserId, Guid ReservationId) : ICommand<ConfirmSpendResponse>;

public sealed record ConfirmSpendResponse(Guid AccountId, long Posted, long Available);

public sealed record ConfirmSpendRequest(Guid ReservationId);
