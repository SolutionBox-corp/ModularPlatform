using System.Text;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Multi-tenancy wiring: registration provisions a tenant and assigns the user to it; the access token carries
/// the tenant claim; two registrations get two distinct tenants. (Cross-user-within-tenant isolation is covered
/// once an admin "list users in my tenant" query exists.)
/// </summary>
public sealed class TenancyTests(PlatformApiFactory fixture) : IClassFixture<PlatformApiFactory>
{
    [Fact]
    public async Task Registration_provisions_a_tenant_and_the_token_carries_it()
    {
        var (userIdA, accessA) = await fixture.RegisterAndLoginAsync($"a-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var (userIdB, _) = await fixture.RegisterAndLoginAsync($"b-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        // Each user belongs to a distinct, non-null tenant (stamped on insert).
        var tenantA = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userIdA}'");
        var tenantB = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userIdB}'");
        tenantA.ShouldNotBe(Guid.Empty);
        tenantA.ShouldNotBe(tenantB);

        // Each tenant row exists.
        var tenants = await fixture.ScalarAsync<long>("SELECT count(*)::bigint FROM tenants");
        tenants.ShouldBeGreaterThanOrEqualTo(2);

        // The access token carries the tenant_id claim matching the user's tenant.
        var claims = DecodeJwtPayload(accessA);
        claims.TryGetProperty("tenant_id", out var tenantClaim).ShouldBeTrue();
        tenantClaim.GetString().ShouldBe(tenantA.ToString());
    }

    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
    }
}
