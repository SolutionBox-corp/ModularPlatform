using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// Platform-plane billing status (the tenant paying the SaaS operator for its entitlement tier). The read-only seam
/// <c>GET /v1/tenant/admin/platform-billing</c> is gated by <c>platform.tenants.manage</c>: an admin sees the current
/// tenant's plan (entitlement tier, default <c>"free"</c>) + the modules that make it up; a regular user cannot.
/// Tenant self-service UI reads the same snapshot through <c>GET /v1/tenant/me/platform-billing</c>. The tenant comes
/// from the token, never a route id.
/// </summary>
[Collection("Integration")]
public sealed class PlatformBillingStatusTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Admin_reads_the_platform_billing_status_with_a_plan_and_modules()
    {
        var admin = await AdminTokenAsync();

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/admin/platform-billing", admin));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("plan").GetString().ShouldBe("free");
        data.GetProperty("provider").GetString().ShouldBe("fake");
        data.GetProperty("checkoutReady").GetBoolean().ShouldBeTrue();
        data.GetProperty("actionRequired").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        data.GetProperty("modules").EnumerateArray().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Status_reports_action_required_when_platform_payment_config_is_missing()
    {
        var admin = await AdminTokenAsync();
        using var host = fixture.CreateHost(("Platform:Payments:Provider", ""));
        using var client = host.CreateClient();

        var response = await client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/admin/platform-billing", admin));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("checkoutReady").GetBoolean().ShouldBeFalse();
        data.GetProperty("provider").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        data.GetProperty("actionRequired").GetString().ShouldBe("payment.gateway_not_configured");
    }

    [Fact]
    public async Task A_non_admin_cannot_read_the_platform_billing_status()
    {
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"pb-{Guid.CreateVersion7():N}@x.com", Password);

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/admin/platform-billing", userToken));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tenant_user_reads_own_platform_billing_status_for_checkout()
    {
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"pb-self-{Guid.CreateVersion7():N}@x.com", Password);

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/me/platform-billing", userToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("plan").GetString().ShouldBe("free");
        data.GetProperty("checkoutReady").GetBoolean().ShouldBeTrue();
        data.GetProperty("modules").EnumerateArray().ShouldNotBeEmpty();
    }
}
