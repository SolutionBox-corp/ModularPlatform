using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Payments;
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
    public async Task Platform_plans_returns_server_authoritative_checkout_catalogue()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"plat-plans-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/me/platform-plans", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        var pro = data.EnumerateArray().Single(p => p.GetProperty("planKey").GetString() == "pro");
        pro.GetProperty("amountMinorUnits").GetInt64().ShouldBe(4900);
        pro.GetProperty("currency").GetString().ShouldBe("EUR");
        pro.GetProperty("description").GetString().ShouldBe("Pro plan");
    }

    [Fact]
    public async Task Platform_plans_omits_misconfigured_zero_price_entries()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"plat-plans-invalid-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        using var host = fixture.CreateHost(
            ("Platform:Payments:Plans:broken:AmountMinorUnits", "0"),
            ("Platform:Payments:Plans:broken:Currency", "EUR"),
            ("Platform:Payments:Plans:broken:Description", "Broken plan"));
        using var client = host.CreateClient();

        var response = await client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/me/platform-plans", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.EnumerateArray()
            .Any(p => p.GetProperty("planKey").GetString() == "broken")
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_tenant_can_start_a_platform_plane_checkout_on_the_platform_gateway()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"plat-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var tenantId = await TenantIdOf(userId);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/me/platform-checkout", token,
            new { planKey = "pro" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("providerPaymentId").GetString().ShouldStartWith("fake_");
        data.GetProperty("redirectUrl").GetString().ShouldNotBeNullOrEmpty();

        var fakeGateway = fixture.Services.GetRequiredService<FakePaymentGateway>();
        fakeGateway.CreatedCheckouts.Any(c =>
            c.AmountMinorUnits == 4900
            && c.Currency == "EUR"
            && c.Metadata.TryGetValue("tenant_id", out var checkoutTenantId)
            && checkoutTenantId == tenantId.ToString("N")
            && c.Metadata.TryGetValue("plane", out var plane)
            && plane == "platform").ShouldBeTrue();

        var proEntitlements = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM tenant_entitlements WHERE \"TenantId\" = '{tenantId}' AND \"Tier\" = 'pro'");
        proEntitlements.ShouldBe(0);
    }

    [Fact]
    public async Task Platform_checkout_requires_authentication()
    {
        var response = await fixture.Client.PostAsJsonAsync("/v1/tenant/me/platform-checkout",
            new { planKey = "pro" });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Platform_checkout_requires_a_configured_platform_gateway()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"plat-missing-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        using var host = fixture.CreateHost(("Platform:Payments:Provider", ""));
        using var client = host.CreateClient();

        var response = await client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/me/platform-checkout", token,
            new { planKey = "pro" }));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Platform_checkout_rejects_unknown_plan_keys()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"plat-plan-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/me/platform-checkout", token,
            new { planKey = "does-not-exist" }));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<Guid> TenantIdOf(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
}
