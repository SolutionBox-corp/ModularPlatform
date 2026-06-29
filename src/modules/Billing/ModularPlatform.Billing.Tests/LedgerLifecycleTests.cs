using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Features.Credits.ExpireCredits;
using ModularPlatform.Billing.Jobs;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using Quartz;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Credit-ledger lifecycle through the real HTTP API + SQL assertions: release restores availability (and is
/// idempotent), confirm draws buckets soonest-to-expire (FIFO), an over-reservation is rejected with no
/// side effects, the expiry sweep materializes lapsed holds and destroyed buckets idempotently, and the
/// double-entry / projection invariants hold after a mixed run.
///
/// Routes/fields/error-codes used (verified against production):
///   POST /v1/billing/credits/topup {amount, bucketExpiryDays, idempotencyKey}  (CreditTopUpEndpoint.cs:14, CreditTopUpCommand.cs:13)
///   POST /v1/billing/credits/reservations {amount, holdMinutes}                (ReserveCreditsEndpoint.cs:14, ReserveCreditsCommand.cs:10) -> {reservationId, available} (ReserveCreditsCommand.cs:8)
///   POST /v1/billing/credits/reservations/confirm {reservationId}              (ConfirmSpendEndpoint.cs:14, ConfirmSpendCommand.cs:9)
///   POST /v1/billing/credits/reservations/release {reservationId}              (ReleaseHoldEndpoint.cs:14, ReleaseHoldCommand.cs:9) -> {accountId, available}
///   credit.insufficient_balance -> 422 (ReserveCreditsHandler.cs:45 BusinessRuleException; Errors.cs:58 -> 422)
///   ExpireCreditsCommand has NO HTTP route; dispatched directly (BillingExpireCreditsJob.cs:17). System scope bypasses RLS (HttpTenantContext.cs:27).
/// </summary>
[Collection("Integration")]
public sealed class LedgerLifecycleTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    private static string Email(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>Waits for the auto-provisioned credit account then seeds its projection columns.</summary>
    private async Task SeedBalanceAsync(Guid userId, long posted, long available, long pending)
    {
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Posted\" = {posted}, \"Available\" = {available}, \"Pending\" = {pending} " +
            $"WHERE \"UserId\" = '{userId}'");
    }

    private async Task<Guid> AccountIdAsync(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"Id\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");

    private async Task<(long Posted, long Available, long Pending)> ProjectionAsync(Guid userId)
    {
        var posted = await fixture.ScalarAsync<long>($"SELECT \"Posted\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        var available = await fixture.ScalarAsync<long>($"SELECT \"Available\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        var pending = await fixture.ScalarAsync<long>($"SELECT \"Pending\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        return (posted, available, pending);
    }

    private async Task<Guid> ReserveAsync(string token, long amount)
    {
        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount }));
        response.EnsureSuccessStatusCode();
        return (await PlatformApiFactory.ReadData(response)).GetProperty("reservationId").GetGuid();
    }

    // ---------------------------------------------------------------------------------------------
    // BL-6 — Reserve -> ReleaseHold restores availability; releasing again is idempotent.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task Releasing_a_hold_restores_availability_and_is_idempotent()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email("release"), Password);
        await SeedBalanceAsync(userId, posted: 1000, available: 1000, pending: 0);

        var reservationId = await ReserveAsync(token, 300);

        // After reserve: available dropped, pending rose. available = posted - pending.
        var afterReserve = await ProjectionAsync(userId);
        afterReserve.Available.ShouldBe(700);
        afterReserve.Pending.ShouldBe(300);
        afterReserve.Available.ShouldBe(afterReserve.Posted - afterReserve.Pending);

        // Release restores availability fully.
        var release1 = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations/release", token, new { reservationId }));
        release1.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(release1)).GetProperty("available").GetInt64().ShouldBe(1000);

        var afterRelease = await ProjectionAsync(userId);
        afterRelease.Available.ShouldBe(1000);
        afterRelease.Pending.ShouldBe(0);
        afterRelease.Posted.ShouldBe(1000);

        // Releasing the SAME hold again is idempotent: still 200, balance unchanged, no double-restore.
        var release2 = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations/release", token, new { reservationId }));
        release2.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(release2)).GetProperty("available").GetInt64().ShouldBe(1000);

        var afterSecondRelease = await ProjectionAsync(userId);
        afterSecondRelease.Available.ShouldBe(1000);
        afterSecondRelease.Pending.ShouldBe(0);

        // Exactly ONE Release ledger entry was written (no double-restore in the ledger either).
        var accountId = await AccountIdAsync(userId);
        var releaseEntries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Release'");
        releaseEntries.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------------------------
    // BL-7 — Confirm draws buckets soonest-to-expire first (FIFO over expiry); each bucket drawn once.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task Confirm_draws_buckets_soonest_to_expire_first()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email("fifo"), Password);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // Two buckets via top-up: the EARLIER-expiring one (30 days) should be drained first, then the later (60).
        await fixture.GrantCreditsAsync(userId, 100L, bucketExpiryDays: 30, idempotencyKey: $"early-{Guid.CreateVersion7():N}");
        await fixture.GrantCreditsAsync(userId, 100L, bucketExpiryDays: 60, idempotencyKey: $"late-{Guid.CreateVersion7():N}");

        var accountId = await AccountIdAsync(userId);
        var earlyBucketId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM credit_buckets WHERE \"AccountId\" = '{accountId}' ORDER BY \"ExpiresAt\" ASC LIMIT 1");
        var lateBucketId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM credit_buckets WHERE \"AccountId\" = '{accountId}' ORDER BY \"ExpiresAt\" DESC LIMIT 1");
        earlyBucketId.ShouldNotBe(lateBucketId);

        // posted == 200 from the two top-ups.
        (await ProjectionAsync(userId)).Posted.ShouldBe(200);

        // Reserve + confirm 150 — spans the early bucket (100) and 50 of the late bucket.
        var reservationId = await ReserveAsync(token, 150);
        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token, new { reservationId }));
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        // FIFO: the EARLIEST-expiring bucket is fully drawn (Remaining 0); the later bucket keeps 50.
        var earlyRemaining = await fixture.ScalarAsync<long>(
            $"SELECT \"Remaining\" FROM credit_buckets WHERE \"Id\" = '{earlyBucketId}'");
        var lateRemaining = await fixture.ScalarAsync<long>(
            $"SELECT \"Remaining\" FROM credit_buckets WHERE \"Id\" = '{lateBucketId}'");
        earlyRemaining.ShouldBe(0);
        lateRemaining.ShouldBe(50);

        // Posted reduced by the confirmed spend (200 -> 50); total remaining across buckets matches.
        var afterConfirm = await ProjectionAsync(userId);
        afterConfirm.Posted.ShouldBe(50);
        (earlyRemaining + lateRemaining).ShouldBe(50);

        // Exactly one Spend entry for the confirmed reservation.
        var spendEntries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Spend'");
        spendEntries.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------------------------
    // BL-8 — Reserve MORE than available -> 422 credit.insufficient_balance; NO hold, NO ledger entry.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task Reserving_more_than_available_is_rejected_with_no_side_effects()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email("insufficient"), Password);
        await SeedBalanceAsync(userId, posted: 100, available: 100, pending: 0);

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount = 500L }));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().ShouldBe("credit.insufficient_balance");

        // No hold and no ledger entry were created for this account; projection untouched.
        var accountId = await AccountIdAsync(userId);
        var holds = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_holds WHERE \"AccountId\" = '{accountId}'");
        var entries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}'");
        holds.ShouldBe(0);
        entries.ShouldBe(0);

        var projection = await ProjectionAsync(userId);
        projection.Posted.ShouldBe(100);
        projection.Available.ShouldBe(100);
        projection.Pending.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------------------------
    // BL-9 — Expiry sweep: lapsed hold restores availability; expired bucket destroys remaining credits
    //        (posted drops by the lost amount); the sweep is idempotent (UNIQUE expire keys).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task Expiry_sweep_restores_lapsed_holds_destroys_expired_buckets_and_is_idempotent()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email("expire"), Password);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // Two expiring buckets totalling 200 posted.
        await fixture.GrantCreditsAsync(userId, 100L, bucketExpiryDays: 10, idempotencyKey: $"keep-{Guid.CreateVersion7():N}");
        var doomedKey = $"doom-{Guid.CreateVersion7():N}";
        await fixture.GrantCreditsAsync(userId, 100L, bucketExpiryDays: 10, idempotencyKey: doomedKey);

        var accountId = await AccountIdAsync(userId);

        // Identify the bucket created for the doomed top-up via its ledger entry, then back-date its expiry.
        var doomedBucketId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"BucketId\" FROM credit_entries WHERE \"IdempotencyKey\" = '{doomedKey}'");

        // Reserve 50, then back-date the hold so it is lapsed (still Active, ExpiresAt in the past).
        var reservationId = await ReserveAsync(token, 50);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_holds SET \"ExpiresAt\" = now() - interval '1 hour' WHERE \"Id\" = '{reservationId}'");
        // Back-date the doomed bucket so it is expired with Remaining still 100.
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_buckets SET \"ExpiresAt\" = now() - interval '1 hour' WHERE \"Id\" = '{doomedBucketId}'");

        // Snapshot before the sweep. After reserve(50): posted 200, pending 50, available 150.
        var before = await ProjectionAsync(userId);
        before.Posted.ShouldBe(200);
        before.Pending.ShouldBe(50);
        before.Available.ShouldBe(150);

        // Trigger the sweep the same way the Jobs host does: dispatch ExpireCreditsCommand (no HTTP route exists).
        // A manual DI scope has no HttpContext, so HttpTenantContext.IsSystem == true -> RLS bypassed, all accounts swept.
        var firstSweep = await DispatchExpireSweepAsync();
        firstSweep.ExpiredHolds.ShouldBeGreaterThanOrEqualTo(1);
        firstSweep.ExpiredBuckets.ShouldBeGreaterThanOrEqualTo(1);
        firstSweep.ExpiredCredits.ShouldBeGreaterThanOrEqualTo(100);

        var afterSweep = await ProjectionAsync(userId);
        // Lapsed hold released: pending 50 -> 0, available += 50. Expired bucket destroyed: posted -= 100, available -= 100.
        // Net: posted 200 -> 100; pending -> 0; available 150 + 50 - 100 = 100. Invariant available = posted - pending holds.
        afterSweep.Posted.ShouldBe(100);
        afterSweep.Pending.ShouldBe(0);
        afterSweep.Available.ShouldBe(100);
        afterSweep.Available.ShouldBe(afterSweep.Posted - afterSweep.Pending);

        // The doomed bucket's remaining was destroyed; the kept bucket is untouched.
        var doomedRemaining = await fixture.ScalarAsync<long>(
            $"SELECT \"Remaining\" FROM credit_buckets WHERE \"Id\" = '{doomedBucketId}'");
        doomedRemaining.ShouldBe(0);

        // The lapsed hold is now Expired and an Expiry entry exists for the bucket.
        var holdStatus = await fixture.ScalarAsync<string>(
            $"SELECT \"Status\" FROM credit_holds WHERE \"Id\" = '{reservationId}'");
        holdStatus.ShouldBe("Expired");
        var expiryEntries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Expiry'");
        expiryEntries.ShouldBe(1);
        var expiryBucketKeyCount = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'expire-bucket:{doomedBucketId}'");
        expiryBucketKeyCount.ShouldBe(1);
        var expiryHoldKeyCount = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'expire-hold:{reservationId}'");
        expiryHoldKeyCount.ShouldBe(1);

        // Idempotent: running the sweep again does NOT double-apply (UNIQUE expire-hold/expire-bucket keys; the
        // hold is no longer Active and the bucket Remaining is 0, so neither is picked up again).
        var secondSweep = await DispatchExpireSweepAsync();
        secondSweep.ExpiredHolds.ShouldBe(0);
        secondSweep.ExpiredBuckets.ShouldBe(0);
        secondSweep.ExpiredCredits.ShouldBe(0);

        var afterSecondSweep = await ProjectionAsync(userId);
        afterSecondSweep.Posted.ShouldBe(100);
        afterSecondSweep.Pending.ShouldBe(0);
        afterSecondSweep.Available.ShouldBe(100);

        var expiryEntriesAfter = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Expiry'");
        expiryEntriesAfter.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------------------------
    // BL-12 — Ledger invariants after a mixed topup/reserve/confirm/release run.
    //   NOTE on the double-entry invariant: in THIS ledger a Reservation entry is a Debit that only moves
    //   Available->Pending (it does NOT reduce Posted), and a Spend is a SEPARATE Debit that reduces Posted.
    //   So a confirmed reservation leaves both a Reservation-debit AND a Spend-debit, while Posted dropped only
    //   once — hence the naive "posted == sum(credits) - sum(debits)" does NOT hold here (verified against
    //   ReserveCreditsHandler.cs:59-69 + ConfirmSpendHandler.cs:63-81). The genuine double-entry-backed invariant
    //   is that POSTED equals the credits still living in buckets: every Topup mints a bucket; Spend/Expiry draw
    //   bucket Remaining down in lock-step with Posted. We assert that, plus available == posted - pending.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task Ledger_invariants_hold_after_a_mixed_run()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(Email("invariant"), Password);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // Top up 1000 (Credit entry + a bucket of 1000). posted=1000, available=1000.
        await fixture.GrantCreditsAsync(userId, 1000L, idempotencyKey: $"mix-{Guid.CreateVersion7():N}");

        // Reserve 400 and confirm it (Reservation debit + Spend debit; posted -= 400, bucket drawn -= 400).
        var confirmedReservation = await ReserveAsync(token, 400);
        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token, new { reservationId = confirmedReservation }));
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Reserve 200 and release it (Reservation debit + Release credit; posted unchanged, availability restored).
        var releasedReservation = await ReserveAsync(token, 200);
        var release = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", token, new { reservationId = releasedReservation }));
        release.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Leave a 100 reservation OUTSTANDING (Active hold) so pending is non-zero at assertion time.
        await ReserveAsync(token, 100);

        var accountId = await AccountIdAsync(userId);
        var projection = await ProjectionAsync(userId);

        // Invariant 1 (double-entry backed): posted == sum of remaining credits across buckets.
        var bucketRemaining = await fixture.ScalarAsync<long>(
            $"SELECT COALESCE(SUM(\"Remaining\"), 0)::bigint FROM credit_buckets WHERE \"AccountId\" = '{accountId}'");
        projection.Posted.ShouldBe(bucketRemaining);

        // Invariant 2 (projection): available == posted - pending.
        projection.Available.ShouldBe(projection.Posted - projection.Pending);

        // Concrete numbers: topup 1000 -> posted 1000; confirm 400 -> posted 600, bucket remaining 600;
        // release 200 restores availability; one 100 reservation stays Active -> pending 100, available 500.
        projection.Posted.ShouldBe(600);
        projection.Pending.ShouldBe(100);
        projection.Available.ShouldBe(500);
    }

    [Fact]
    public async Task Expiring_a_bucket_that_backs_an_active_reservation_does_not_crash_or_go_negative()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync(Email("expire-reserved"), Password);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        // 100 credits in a bucket that will be expired; reserve ALL 100 with a long hold so it is still ACTIVE when
        // the bucket expires. Reservations do NOT draw the bucket, so bucket.Remaining still counts the held 100.
        await fixture.GrantCreditsAsync(userId, 100, bucketExpiryDays: 1);
        await fixture.DispatchAsync(new ReserveCreditsCommand(userId, 100, HoldMinutes: 60 * 24 * 30));

        var accountId = await AccountIdAsync(userId);
        // Backdate the bucket so the sweep sees it expired while the hold is still active.
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_buckets SET \"ExpiresAt\" = now() - interval '1 day' WHERE \"AccountId\" = '{accountId}'");

        // The sweep must NOT crash and must NOT drive the projection negative — the reserved credits survive until
        // the hold resolves (the bucket is skipped this sweep, expired on a later one once the credits are free).
        await DispatchExpireSweepAsync();

        var projection = await ProjectionAsync(userId);
        projection.Posted.ShouldBe(100);
        projection.Available.ShouldBe(0);
        projection.Pending.ShouldBe(100);
    }

    [Fact]
    public async Task Non_expiring_topup_bucket_is_not_touched_by_expiry_sweep()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync(Email("non-expiring"), Password);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);
        var key = $"forever-{Guid.CreateVersion7():N}";

        await fixture.GrantCreditsAsync(userId, 150, bucketExpiryDays: null, idempotencyKey: key);

        var accountId = await AccountIdAsync(userId);
        var bucketId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"BucketId\" FROM credit_entries WHERE \"IdempotencyKey\" = '{key}'");
        var nonExpiringBuckets = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_buckets WHERE \"Id\" = '{bucketId}' AND \"ExpiresAt\" IS NULL");
        nonExpiringBuckets.ShouldBe(1);

        await DispatchExpireSweepAsync();

        var bucketRemaining = await fixture.ScalarAsync<long>(
            $"SELECT \"Remaining\" FROM credit_buckets WHERE \"Id\" = '{bucketId}'");
        bucketRemaining.ShouldBe(150);

        var expiryEntries = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Expiry'");
        expiryEntries.ShouldBe(0);

        var projection = await ProjectionAsync(userId);
        projection.Posted.ShouldBe(150);
        projection.Available.ShouldBe(150);
        projection.Pending.ShouldBe(0);
    }

    [Fact]
    public void Expire_credits_job_is_a_non_overlapping_scheduler_adapter()
    {
        typeof(BillingExpireCreditsJob)
            .GetCustomAttributes(typeof(DisallowConcurrentExecutionAttribute), inherit: false)
            .Length.ShouldBe(1);
    }

    /// <summary>
    /// Dispatches the credit-expiry sweep command directly — there is NO HTTP endpoint for it (it is only
    /// triggered by <c>BillingExpireCreditsJob</c> in the Jobs host). A manual DI scope created off the Api host
    /// has no HttpContext, so <c>HttpTenantContext.IsSystem</c> is true and the data connection runs as a system
    /// principal (RLS bypassed) — exactly the Jobs-host execution context. The command sweeps every account.
    /// </summary>
    private async Task<ExpireCreditsResponse> DispatchExpireSweepAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        return await dispatcher.Send(new ExpireCreditsCommand());
    }
}
