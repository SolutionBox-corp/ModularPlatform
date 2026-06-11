using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// Platform-plane charging: a tenant pays the SaaS operator through the PLATFORM's own gateway (resolved on
/// <c>PaymentPlane.Platform</c>, configured via <c>Platform:Payments</c>). The checkout resolves and returns a redirect
/// — distinct from the tenant-plane (a tenant's own gateway charging its members).
/// </summary>
[Collection("Integration")]
public sealed class PlatformCheckoutTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task A_tenant_can_start_a_platform_plane_checkout_on_the_platform_gateway()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"plat-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/me/platform-checkout", token,
            new { amountMinorUnits = 4_900L, currency = "EUR", description = "Pro plan" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("providerPaymentId").GetString().ShouldStartWith("fake_");
        data.GetProperty("redirectUrl").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Platform_checkout_requires_authentication()
    {
        var response = await fixture.Client.PostAsJsonAsync("/v1/tenant/me/platform-checkout",
            new { amountMinorUnits = 100L, currency = "EUR", description = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
