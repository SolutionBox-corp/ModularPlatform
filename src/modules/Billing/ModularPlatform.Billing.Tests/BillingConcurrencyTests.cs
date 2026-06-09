using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// MONEY-CRITICAL. Proves the credit ledger never double-spends under concurrency: with a balance of exactly
/// 1000 and 20 simultaneous reservations of 100, at most 10 may succeed and available must never go negative.
/// A debit path whose row lock runs in autocommit (released immediately) would let &gt;10 through or thrash with
/// concurrency 500s — this locks the behaviour down.
/// </summary>
[Collection("Integration")]
public sealed class BillingConcurrencyTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Concurrent_reservations_never_exceed_balance()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"billing-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        // The account is provisioned by the UserRegistered event handler; wait for it, then seed 1000 credits.
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Posted\" = 1000, \"Available\" = 1000, \"Pending\" = 0 " +
            $"WHERE \"UserId\" = '{userId}'");

        // 20 simultaneous reservations of 100 against a 1000 balance.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var request = fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount = 100L });
            var response = await fixture.Client.SendAsync(request);
            return response.StatusCode;
        }));

        var succeeded = attempts.Count(s => s == HttpStatusCode.OK);
        var rejected = attempts.Count(s => s == HttpStatusCode.UnprocessableEntity);

        // No double-spend: succeeded * 100 can never exceed the 1000 balance, and every attempt resolved cleanly
        // (no concurrency 500s leaking through).
        succeeded.ShouldBe(10);
        rejected.ShouldBe(10);

        // The ledger's own availability must be non-negative and match what succeeded.
        var posted = await fixture.ScalarAsync<long>(
            $"SELECT \"Posted\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        var activeHolds = await fixture.ScalarAsync<long>(
            $"SELECT COALESCE(SUM(h.\"Amount\"), 0)::bigint FROM credit_holds h " +
            $"JOIN credit_accounts a ON a.\"Id\" = h.\"AccountId\" " +
            $"WHERE a.\"UserId\" = '{userId}' AND h.\"Status\" = 'Active'");
        (posted - activeHolds).ShouldBeGreaterThanOrEqualTo(0);
        activeHolds.ShouldBe(succeeded * 100L);
    }
}
