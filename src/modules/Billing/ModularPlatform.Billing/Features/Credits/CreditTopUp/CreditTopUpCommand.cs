using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.CreditTopUp;

public sealed record CreditTopUpCommand(
    Guid UserId,
    long Amount,
    int? BucketExpiryDays,
    string IdempotencyKey) : ICommand<CreditTopUpResponse>;

public sealed record CreditTopUpResponse(Guid AccountId, long Posted, bool AlreadyApplied);

public sealed record CreditTopUpRequest(long Amount, int? BucketExpiryDays, string IdempotencyKey);
