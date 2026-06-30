using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Subscriptions.GetMySubscription;

/// <summary>
/// Read slice: the caller's most recent non-canceled subscription (one live subscription per user is
/// enforced by the subscriptions UserId partial unique index). 404 when none exists.
/// </summary>
internal sealed class GetMySubscriptionHandler(IReadDbContextFactory<BillingDbContext> readFactory)
    : IQueryHandler<GetMySubscriptionQuery, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(GetMySubscriptionQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.Subscriptions
            .Where(s => s.UserId == query.UserId && s.Status != SubscriptionStatus.Canceled)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SubscriptionResponse(
                s.Id, s.PlanKey, s.Status.ToString(), s.CurrentPeriodEnd, s.CancelAtPeriodEnd))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("billing.subscription.not_found", "No active subscription.");
    }
}
