using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Payments;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC25: per-tenant payment webhooks. The endpoint is anonymous because providers call it, but the handler never trusts
/// the payload directly: it binds the URL tenant to that tenant's configured gateway, verifies/re-fetches there, and
/// grants through the existing idempotent purchase saga.
/// </summary>
[Collection("Integration")]
public sealed class TenantWebhookTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    private FakePaymentGateway FakePay => fixture.Services.GetRequiredService<FakePaymentGateway>();

    [Fact]
    public async Task Unknown_tenant_webhook_is_acknowledged_and_ignored()
    {
        var response = await fixture.Client.PostAsync(
            $"/v1/billing/webhooks/fake/{Guid.CreateVersion7()}?id=missing", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GoPay_wrong_url_token_is_acknowledged_before_provider_call()
    {
        var admin = await AdminTokenAsync();
        var tenantId = TenantOf(admin);

        var configure = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", admin,
            new
            {
                provider = "gopay",
                currency = "CZK",
                goPayGoid = 123L,
                goPayClientId = "cid",
                goPayClientSecret = "secret",
                sandbox = true,
            }));
        configure.StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await fixture.Client.PostAsync(
            $"/v1/billing/webhooks/gopay/{tenantId}/wrong-token?id=123", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Stripe_bad_signature_is_acknowledged_and_ignored()
    {
        var admin = await AdminTokenAsync();
        var tenantId = TenantOf(admin);

        var configure = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", admin,
            new
            {
                provider = "stripe",
                currency = "EUR",
                stripeApiKey = "sk_test_tenant",
                stripeWebhookSecret = "whsec_tenant",
                sandbox = true,
            }));
        configure.StatusCode.ShouldBe(HttpStatusCode.OK);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/billing/webhooks/stripe/{tenantId}")
        {
            Content = new StringContent("""{"id":"evt_bad","type":"checkout.session.completed","data":{"object":{"id":"cs_bad"}}}""",
                Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", "t=1,v1=not-a-real-signature");

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Duplicate_paid_webhook_grants_purchase_once()
    {
        var (purchaseId, providerPaymentId, tenantId) = await StartPackageCheckoutAsync(510);
        FakePay.SetState(providerPaymentId, PaymentState.Paid);

        (await fixture.Client.PostAsync($"/v1/billing/webhooks/fake/{tenantId}?id={providerPaymentId}", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'", 1);

        (await fixture.Client.PostAsync($"/v1/billing/webhooks/fake/{tenantId}?id={providerPaymentId}", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await Task.Delay(750);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Out_of_order_unpaid_then_paid_webhook_converges()
    {
        var (purchaseId, providerPaymentId, tenantId) = await StartPackageCheckoutAsync(520);

        (await fixture.Client.PostAsync($"/v1/billing/webhooks/fake/{tenantId}?id={providerPaymentId}", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await Task.Delay(750);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'")).ShouldBe(0);

        FakePay.SetState(providerPaymentId, PaymentState.Paid);
        (await fixture.Client.PostAsync($"/v1/billing/webhooks/fake/{tenantId}?id={providerPaymentId}", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'", 1);
    }

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    private async Task<(Guid PurchaseId, string ProviderPaymentId, Guid TenantId)> StartPackageCheckoutAsync(long creditAmount)
    {
        var admin = await AdminTokenAsync();
        var tenantId = TenantOf(admin);

        var configure = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", admin,
            new { provider = "fake", currency = "EUR", sandbox = false }));
        configure.StatusCode.ShouldBe(HttpStatusCode.OK);

        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/admin/packages", admin,
            new
            {
                name = $"Webhook package {Guid.CreateVersion7():N}",
                creditAmount,
                price = 9.99m,
                currency = "EUR",
                active = true,
                stripePriceId = $"price_{Guid.CreateVersion7():N}",
            }));
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var packageId = (await PlatformApiFactory.ReadData(create)).GetProperty("id").GetGuid();

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", admin));
        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(checkout);
        var purchaseId = data.GetProperty("purchaseId").GetGuid();
        var providerPaymentId = data.GetProperty("checkoutSessionId").GetString()!;

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}'", 1);

        return (purchaseId, providerPaymentId, tenantId);
    }

    private static Guid TenantOf(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
            .Replace('-', '+')
            .Replace('_', '/');
        var claims = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
        return Guid.Parse(claims.GetProperty("tenant_id").GetString()!);
    }
}
