using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Credits.EnsureCreditAccount;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using Npgsql;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Ledger backstops from docs/test-scenarios.md:
/// BL-5 (DB CHECK rejects a negative projection even from raw SQL), BL-10 (GetCreditBalance returns the
/// STORED available, not a recompute), BL-11 (overflow-safe top-up), EV-5 (concurrent EnsureCreditAccount
/// dedups on UNIQUE UserId), PL-2 (audit Update rows record ONLY changed columns; converted enums as strings).
/// </summary>
[Collection("Integration")]
public sealed class LedgerBackstopTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task BL5_raw_negative_projection_write_is_rejected_by_the_db_check()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"bl5-{Guid.CreateVersion7():N}@test.io", Password);
        await TopUpAsync(userId, 100);

        var act = () => fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Available\" = -1 WHERE \"UserId\" = '{userId}'");

        var ex = await Should.ThrowAsync<PostgresException>(act);
        ex.SqlState.ShouldBe("23514"); // check_violation
    }

    [Fact]
    public async Task BL10_balance_read_returns_the_stored_projection_not_a_recompute()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"bl10-{Guid.CreateVersion7():N}@test.io", Password);
        await TopUpAsync(userId, 100);

        // Skew the STORED projection away from what a ledger recompute would say (admin write, audit-free).
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Available\" = 42 WHERE \"UserId\" = '{userId}'");

        var balance = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/billing/credits/balance", token));
        (await PlatformApiFactory.ReadData(balance)).GetProperty("available").GetInt64().ShouldBe(42);
    }

    [Fact]
    public async Task BL11_topup_that_would_overflow_is_rejected_and_balance_unchanged()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"bl11-{Guid.CreateVersion7():N}@test.io", Password);
        await TopUpAsync(userId, 100);

        var nearMax = long.MaxValue - 10;
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Posted\" = {nearMax}, \"Available\" = {nearMax} WHERE \"UserId\" = '{userId}'");

        var overflow = await Should.ThrowAsync<BusinessRuleException>(
            () => fixture.GrantCreditsAsync(userId, 100));
        overflow.ErrorCode.ShouldBe("credit.amount.too_large");

        (await fixture.ScalarAsync<long>(
            $"SELECT \"Posted\" FROM credit_accounts WHERE \"UserId\" = '{userId}'")).ShouldBe(nearMax);
    }

    [Fact]
    public async Task EV5_concurrent_account_provisioning_yields_exactly_one_account()
    {
        var userId = Guid.CreateVersion7(); // no real user needed — the command provisions by id

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = fixture.Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            await dispatcher.Send(new EnsureCreditAccountCommand(userId), CancellationToken.None);
        }));

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task EV5_existing_account_provisioning_is_a_noop()
    {
        var userId = Guid.CreateVersion7();

        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new EnsureCreditAccountCommand(userId), CancellationToken.None);
        var accountId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");

        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Posted\" = 10, \"Available\" = 10 WHERE \"Id\" = '{accountId}'");

        await dispatcher.Send(new EnsureCreditAccountCommand(userId), CancellationToken.None);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'")).ShouldBe(1);
        (await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM credit_accounts WHERE \"UserId\" = '{userId}'")).ShouldBe(accountId);
        (await fixture.ScalarAsync<string>(
            $"SELECT \"Posted\" || ':' || \"Pending\" || ':' || \"Available\" FROM credit_accounts WHERE \"Id\" = '{accountId}'"))
            .ShouldBe("10:0:10");
    }

    [Fact]
    public async Task PL2_audit_update_rows_record_only_changed_columns_and_enums_as_strings()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"pl2-{Guid.CreateVersion7():N}@test.io", Password);
        await TopUpAsync(userId, 100);

        // Reserve + release: the hold flips Active -> Released (a converted enum) via tracked saves.
        var reserve = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount = 50 }));
        var reservationId = (await PlatformApiFactory.ReadData(reserve)).GetProperty("reservationId").GetGuid();
        var release = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", token, new { reservationId }));
        release.StatusCode.ShouldBe(HttpStatusCode.OK);

        var newValues = await fixture.ScalarAsync<string>(
            "SELECT \"NewValues\"::text FROM billing_audit_entries " +
            $"WHERE \"EntityType\" = 'CreditHold' AND \"EntityId\" = '{reservationId}' AND \"Action\" = 'Update' LIMIT 1");

        // Converted enum lands as its string name, never the int.
        newValues.ShouldContain("\"Released\"");
        // Only changed columns: the immutable hold Amount is NOT in the update payload.
        newValues.ShouldNotContain("\"Amount\"");
    }

    private Task TopUpAsync(Guid userId, long amount) => fixture.GrantCreditsAsync(userId, amount);
}
