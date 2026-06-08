using ModularPlatform.Billing.Contracts;
using Xunit;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Placeholder so the Billing test project builds and runs. Real slice + integration tests (Testcontainers
/// Postgres exercising the pessimistic debit path, idempotent top-up and the Stripe webhook) are added later
/// per the writing-modularplatform-tests skill.
/// </summary>
public sealed class BillingModulePlaceholderTests
{
    [Fact]
    public void Contracts_event_carries_idempotency_key()
    {
        var occurredAt = DateTimeOffset.UnixEpoch;
        var evt = new CreditsToppedUpIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: occurredAt,
            UserId: Guid.CreateVersion7(),
            AccountId: Guid.CreateVersion7(),
            Amount: 100,
            NewPosted: 100,
            IdempotencyKey: "evt_test");

        Assert.Equal("evt_test", evt.IdempotencyKey);
        Assert.Equal(100, evt.Amount);
        Assert.Equal(occurredAt, evt.OccurredAt);
    }
}
