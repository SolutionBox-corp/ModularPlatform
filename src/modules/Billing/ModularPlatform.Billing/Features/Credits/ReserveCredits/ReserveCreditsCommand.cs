using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

public sealed record ReserveCreditsCommand(Guid UserId, long Amount, int? HoldMinutes = null)
    : ICommand<ReserveCreditsResponse>;

public sealed record ReserveCreditsResponse(Guid ReservationId, long Available);

public sealed record ReserveCreditsRequest(long Amount, int? HoldMinutes);
