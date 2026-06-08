using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ReleaseHold;

public sealed record ReleaseHoldCommand(Guid UserId, Guid ReservationId) : ICommand<ReleaseHoldResponse>;

public sealed record ReleaseHoldResponse(Guid AccountId, long Available);

public sealed record ReleaseHoldRequest(Guid ReservationId);
