using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC28: the credit ledger is a paged, owner-scoped read model over append-only entries. Consumers use this endpoint;
/// they do not recompute money from tables or duplicate Billing's ledger rules.
/// </summary>
[Collection("Integration")]
public sealed class CreditLedgerReadTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Ledger_entries_are_paged_and_scoped_to_the_token_owner()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync($"ledger-alice-{Guid.CreateVersion7():N}@test.io", Password);
        var (bobId, bobToken) = await fixture.RegisterAndLoginAsync($"ledger-bob-{Guid.CreateVersion7():N}@test.io", Password);

        for (var i = 0; i < 5; i++)
        {
            var key = $"alice-ledger-{Guid.CreateVersion7():N}";
            await fixture.GrantCreditsAsync(aliceId, 100 + i, idempotencyKey: key);
            await fixture.ExecuteSqlAsync(
                "UPDATE credit_entries " +
                $"SET \"CreatedAt\" = timestamp with time zone '2026-01-01 00:00:0{i}+00' " +
                $"WHERE \"IdempotencyKey\" = '{key}'");
        }

        await fixture.GrantCreditsAsync(bobId, 999, idempotencyKey: $"bob-ledger-{Guid.CreateVersion7():N}");

        var alicePage = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/credits/entries?page=1&pageSize=2", aliceToken));
        alicePage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aliceData = await PlatformApiFactory.ReadData(alicePage);

        aliceData.GetProperty("page").GetInt32().ShouldBe(1);
        aliceData.GetProperty("pageSize").GetInt32().ShouldBe(2);
        aliceData.GetProperty("totalCount").GetInt64().ShouldBe(5);
        aliceData.GetProperty("totalPages").GetInt32().ShouldBe(3);
        aliceData.GetProperty("items").EnumerateArray().Count().ShouldBe(2);
        aliceData.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("amount").GetInt64())
            .ShouldBe(new[] { 104L, 103L });
        aliceData.GetProperty("items").EnumerateArray()
            .ShouldAllBe(item => item.GetProperty("amount").GetInt64() != 999);

        var bobPage = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/credits/entries?page=1&pageSize=20", bobToken));
        bobPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bobData = await PlatformApiFactory.ReadData(bobPage);

        bobData.GetProperty("totalCount").GetInt64().ShouldBe(1);
        bobData.GetProperty("items").EnumerateArray().Single().GetProperty("amount").GetInt64().ShouldBe(999);
    }

    [Fact]
    public async Task Ledger_entries_with_the_same_timestamp_are_ordered_by_id_for_stable_paging()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"ledger-stable-{Guid.CreateVersion7():N}@test.io", Password);
        var firstKey = $"ledger-stable-first-{Guid.CreateVersion7():N}";
        var secondKey = $"ledger-stable-second-{Guid.CreateVersion7():N}";
        var thirdKey = $"ledger-stable-third-{Guid.CreateVersion7():N}";

        await fixture.GrantCreditsAsync(userId, 100, idempotencyKey: firstKey);
        await fixture.GrantCreditsAsync(userId, 200, idempotencyKey: secondKey);
        await fixture.GrantCreditsAsync(userId, 300, idempotencyKey: thirdKey);

        await fixture.ExecuteSqlAsync(
            "UPDATE credit_entries " +
            "SET \"CreatedAt\" = timestamp with time zone '2026-01-01 00:00:00+00' " +
            $"WHERE \"IdempotencyKey\" IN ('{firstKey}', '{secondKey}', '{thirdKey}')");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/credits/entries?page=1&pageSize=3", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        var items = data.GetProperty("items").EnumerateArray().ToArray();

        items.Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe(items.Select(item => item.GetProperty("id").GetGuid()).OrderByDescending(id => id));
        items.Select(item => item.GetProperty("amount").GetInt64())
            .ShouldBe(new[] { 300L, 200L, 100L });
    }
}
