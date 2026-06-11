using System.Net;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// <c>GET /v1/tenant/me/entitlements</c> is the single source for the FE nav. A freshly provisioned tenant is
/// entitled to the default product modules; the response carries them keyed + enabled. Tenant comes from the token.
/// </summary>
[Collection("Integration")]
public sealed class EntitlementsTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task A_fresh_tenant_is_entitled_to_the_default_product_modules()
    {
        var (_, access) = await fixture.RegisterAndLoginAsync($"ent-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        var enabled = data.GetProperty("modules").EnumerateArray()
            .Where(m => m.GetProperty("enabled").GetBoolean())
            .Select(m => m.GetProperty("key").GetString())
            .ToHashSet();

        enabled.ShouldContain("billing");
        enabled.ShouldContain("notifications");
        enabled.ShouldContain("files");
        enabled.ShouldContain("operations");
        enabled.ShouldContain("gdpr");
    }

    [Fact]
    public async Task The_entitlements_endpoint_requires_authentication()
    {
        var response = await fixture.Client.GetAsync("/v1/tenant/me/entitlements");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
