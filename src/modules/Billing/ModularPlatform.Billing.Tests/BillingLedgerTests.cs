using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Ledger correctness under concurrency: confirming a reservation is exactly-once, and an idempotent top-up
/// applied twice (same key) credits exactly once. These exercise the outbox handlers running inside the explicit
/// transaction that holds the row lock.
/// </summary>
[Collection("Integration")]
public sealed class BillingLedgerTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Confirming_a_reservation_is_exactly_once_under_concurrency()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"confirm-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Posted\" = 1000, \"Available\" = 1000, \"Pending\" = 0 " +
            $"WHERE \"UserId\" = '{userId}'");

        var reserve = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount = 100L }));
        reserve.EnsureSuccessStatusCode();
        var reservationId = (await PlatformApiFactory.ReadData(reserve)).GetProperty("reservationId").GetGuid();

        // 10 simultaneous confirmations of the same reservation.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var request = fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token,
                new { reservationId });
            return (await fixture.Client.SendAsync(request)).StatusCode;
        }));

        attempts.ShouldAllBe(s => s == HttpStatusCode.OK);

        // Exactly one Spend entry; posted decremented exactly once (1000 -> 900); the hold is Confirmed.
        var spendEntries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries e JOIN credit_accounts a ON a.\"Id\" = e.\"AccountId\" " +
            $"WHERE a.\"UserId\" = '{userId}' AND e.\"Type\" = 'Spend'");
        spendEntries.ShouldBe(1);

        var posted = await fixture.ScalarAsync<long>(
            $"SELECT \"Posted\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        posted.ShouldBe(900);
    }

    [Fact]
    public async Task Top_up_with_the_same_idempotency_key_credits_exactly_once()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync(
            $"topup-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        var key = $"key-{Guid.CreateVersion7():N}";

        // Two simultaneous top-ups with the SAME idempotency key against a brand-new user (the internal
        // grant primitive, as a real payment would dispatch it).
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            fixture.GrantCreditsAsync(userId, 500L, idempotencyKey: key)));

        attempts.ShouldAllBe(r => r.AccountId != Guid.Empty);

        // Exactly ONE credit: one account, one Topup entry for the key, posted == 500 (not 1000).
        var accounts = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        accounts.ShouldBe(1);

        var topupEntries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = '{key}'");
        topupEntries.ShouldBe(1);

        var posted = await fixture.ScalarAsync<long>(
            $"SELECT \"Posted\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        posted.ShouldBe(500);
    }

    [Fact]
    public async Task Idempotency_keys_are_scoped_per_account_not_globally()
    {
        var (userA, _) = await fixture.RegisterAndLoginAsync($"acctA-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        var (userB, _) = await fixture.RegisterAndLoginAsync($"acctB-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        var sharedKey = $"order-{Guid.CreateVersion7():N}";

        var a = await fixture.GrantCreditsAsync(userA, 500, idempotencyKey: sharedKey);
        var b = await fixture.GrantCreditsAsync(userB, 700, idempotencyKey: sharedKey);

        // B re-using a key account A already used must STILL be credited — idempotency is per account, not global.
        a.AlreadyApplied.ShouldBeFalse();
        b.AlreadyApplied.ShouldBeFalse("a key used by another account must not silently no-op this account's grant");
        b.Posted.ShouldBe(700);
    }
}
