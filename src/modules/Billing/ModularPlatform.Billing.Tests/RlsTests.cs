using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Proves Postgres Row-Level Security is real defence-in-depth, not just app-level filtering. Two users each
/// get a credit account (provisioned by the cross-module event). Connecting as the least-privilege runtime
/// role, the OWNER sees their account but ANOTHER user — even with a raw, unfiltered query — sees zero rows,
/// because the database policy keyed on <c>app.principal_id</c> hides them. The admin/superuser connection
/// still sees both (it bypasses RLS), confirming the rows genuinely exist and only RLS hides them.
/// </summary>
[Collection("Integration")]
public sealed class RlsTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task A_user_cannot_see_another_users_credit_account_even_with_a_raw_query()
    {
        var (alice, _) = await fixture.RegisterAndLoginAsync($"alice-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        var (bob, _) = await fixture.RegisterAndLoginAsync($"bob-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        // Both accounts are provisioned asynchronously by the UserRegistered event handler.
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{alice}'", 1);
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{bob}'", 1);

        var aliceRowSql = $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{alice}'";

        // Admin connection bypasses RLS — the row really exists.
        (await fixture.ScalarAsync<long>(aliceRowSql)).ShouldBe(1);

        // As Alice (runtime role, principal = Alice) she sees her own account.
        (await fixture.ScalarAsUserAsync<long>(alice, aliceRowSql)).ShouldBe(1);

        // As Bob, the SAME unfiltered query returns zero — the DB policy hides Alice's row from him.
        (await fixture.ScalarAsUserAsync<long>(bob, aliceRowSql)).ShouldBe(0);

        // And a blanket "give me everything" as Bob still only ever yields his own single account.
        var bobSeesAll = await fixture.ScalarAsUserAsync<long>(bob, "SELECT count(*)::bigint FROM credit_accounts");
        bobSeesAll.ShouldBe(1);
    }

    [Fact]
    public async Task System_principal_bypasses_user_owned_rls_without_using_the_admin_role()
    {
        var (alice, _) = await fixture.RegisterAndLoginAsync($"system-alice-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        var (bob, _) = await fixture.RegisterAndLoginAsync($"system-bob-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{alice}'", 1);
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{bob}'", 1);

        var userIds = $"'{alice}', '{bob}'";
        (await fixture.ScalarAsUserAsync<long>(
            alice,
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" IN ({userIds})")).ShouldBe(1);

        (await fixture.ScalarAsSystemAsync<long>(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" IN ({userIds})")).ShouldBe(2);
    }
}
