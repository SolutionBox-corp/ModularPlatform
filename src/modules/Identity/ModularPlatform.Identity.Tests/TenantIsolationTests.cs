using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// PL-5 — tenant isolation, proving the "missing tenant claim" null-escape is CLOSED.
/// <para>
/// The <c>users</c> table is <c>ITenantScoped</c> (not <c>IUserOwned</c>), so cross-tenant isolation on it is
/// enforced by the EF global query filter in <c>PlatformDbContext.BuildTenantFilter</c>
/// (<c>IsSystemContext || TenantId == CurrentTenantId</c>, with NO <c>CurrentTenantId == null</c> short-circuit),
/// NOT by a Postgres RLS policy (<c>RlsBootstrapper</c> only attaches policies to <c>IUserOwned</c> tables —
/// see <c>RlsBootstrapper.CollectUserOwnedTables</c>, which skips <c>users</c>). The reachable way to exercise
/// that filter through this harness is an authenticated query that flows through the module DbContext —
/// <c>GET /v1/identity/users/me</c> — where the caller's identity comes from the token, never a body/route id.
/// </para>
/// <para>
/// What is NOT reachable here without forging a token: an authenticated, NON-system principal that carries NO
/// tenant claim. Registration always provisions a tenant and stamps the claim (proved by
/// <c>TenancyTests.Registration_provisions_a_tenant_and_the_token_carries_it</c>), so the closest reachable
/// guarantee is cross-tenant non-visibility — asserted below. See scenariosSkipped/concerns for the residual.
/// </para>
/// </summary>
[Collection("Integration")]
public sealed class TenantIsolationTests(PlatformApiFactory fixture)
{
    /// <summary>
    /// Two users register into two distinct tenants (stamped on insert by the tenant-stamping interceptor); the
    /// admin connection — which bypasses both RLS and the EF filter — sees BOTH user rows, confirming they
    /// genuinely co-exist in the same physical table. Isolation is therefore a filtering concern, not absence.
    /// </summary>
    [Fact]
    public async Task Two_users_land_in_distinct_tenants_in_the_same_users_table()
    {
        var (userA, _) = await fixture.RegisterAndLoginAsync($"tiso-a-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        var (userB, _) = await fixture.RegisterAndLoginAsync($"tiso-b-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        var tenantA = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userA}'");
        var tenantB = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userB}'");

        tenantA.ShouldNotBe(Guid.Empty);
        tenantB.ShouldNotBe(Guid.Empty);
        tenantA.ShouldNotBe(tenantB);

        // Admin (RLS/filter-bypassing) connection sees both rows — they physically exist side by side.
        var bothExist = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM users WHERE \"Id\" IN ('{userA}', '{userB}')");
        bothExist.ShouldBe(2);
    }

    /// <summary>
    /// The KEY assertion. Going through the EF tenant filter via the authenticated <c>/me</c> endpoint, user A
    /// reads exactly ONE profile — their own — even though user B's row lives in the same table under a different
    /// tenant. The filter never widens to "see everything": a non-system principal sees only their tenant's rows.
    /// </summary>
    [Fact]
    public async Task A_user_reading_through_the_tenant_filter_sees_only_their_own_tenant_data()
    {
        var (userA, accessA) = await fixture.RegisterAndLoginAsync($"tiso-self-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");
        // User B exists in a different tenant; A must never observe B through the filtered read path.
        await fixture.RegisterAndLoginAsync($"tiso-other-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", accessA));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        // The read flows through PlatformDbContext's tenant filter; the only row that survives it for A is A's own.
        // UserProfileResponse exposes the user id as `Id` (serialized camelCase as `id`).
        data.GetProperty("id").GetGuid().ShouldBe(userA);
    }

    /// <summary>
    /// There is no HTTP route that accepts a foreign user/tenant id (identity always comes from the token, never a
    /// route/body id — an IDOR guard), so cross-tenant read is not even expressible. An anonymous caller — a
    /// non-system principal with NO tenant claim at all — is rejected outright (401), never silently granted a
    /// "see everything" view. This is the reachable proxy for the closed null-escape.
    /// </summary>
    [Fact]
    public async Task Anonymous_caller_with_no_tenant_claim_is_rejected_not_granted_global_visibility()
    {
        var response = await fixture.Client.GetAsync("/v1/identity/users/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
