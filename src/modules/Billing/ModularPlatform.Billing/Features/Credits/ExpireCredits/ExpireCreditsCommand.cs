using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ExpireCredits;

/// <summary>
/// Sweep command, invoked by the Jobs host on a cron. Cleanup only (correctness is already guaranteed by
/// the live availability query that ignores expired holds); this materializes the expiry into the ledger.
/// </summary>
public sealed record ExpireCreditsCommand : ICommand<ExpireCreditsResponse>;

public sealed record ExpireCreditsResponse(int ExpiredHolds, int ExpiredBuckets, long ExpiredCredits);
