using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// The per-tenant checkout path end-to-end: a tenant configures its own gateway, then a checkout resolves THAT
/// gateway (via IPaymentGatewayResolver / IPaymentConfigStore / ISecretProtector) and returns a redirect URL. A tenant
/// with no configured gateway is a clean 422 — never a 500, never another tenant's gateway.
/// </summary>
[Collection("Integration")]
public sealed class CreateTenantCheckoutTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

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
    public async Task A_configured_tenant_gets_a_checkout_on_its_own_gateway()
    {
        var admin = await AdminTokenAsync();

        // Configure THIS tenant's gateway to the in-memory fake immediately before use (upsert — robust to test order).
        var configure = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", admin,
            new { provider = "fake", currency = "CZK", sandbox = false }));
        configure.StatusCode.ShouldBe(HttpStatusCode.OK);

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/payments/checkout", admin,
            new { amountMinorUnits = 25_000L, currency = "CZK", description = "Membership" }));

        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(checkout);
        var providerId = data.GetProperty("providerPaymentId").GetString()!;
        providerId.ShouldStartWith("fake_");
        data.GetProperty("redirectUrl").GetString()!.ShouldContain(providerId);
    }

    [Fact]
    public async Task A_tenant_with_no_configured_gateway_is_a_clean_422()
    {
        // A fresh user = a fresh tenant that never configured a gateway.
        var (_, token) = await fixture.RegisterAndLoginAsync($"nopay-{Guid.CreateVersion7():N}@x.com", Password);

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/payments/checkout", token,
            new { amountMinorUnits = 1_000L, currency = "CZK", description = "x" }));

        checkout.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
