using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// A tenant self-service-configures its own payment gateway; the credentials are SEALED at rest via ISecretProtector
/// (the stored bytes are an AES-GCM envelope, never the plaintext key), and the config row goes Active. This is the
/// per-tenant, provider-agnostic foundation the resolver reads through.
/// </summary>
[Collection("Integration")]
public sealed class PaymentGatewayConfigTests(PlatformApiFactory fixture)
{
    // The shared admin account password convention in this assembly — a mismatch locks the account for other tests.
    private const string Password = "S3cure!pass";
    private const string ApiKey = "sk_test_SEEKRET"; // 15 bytes → sealed envelope is 12(nonce)+16(tag)+15 = 43 bytes

    private async Task<(string Token, Guid TenantId)> AdminAsync()
    {
        await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        var token = (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;

        var payload = token.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        var claims = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
        return (token, Guid.Parse(claims.GetProperty("tenant_id").GetString()!));
    }

    [Fact]
    public async Task Configuring_a_stripe_gateway_activates_it_and_seals_the_key()
    {
        var (token, tenantId) = await AdminAsync();

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", token,
            new { provider = "stripe", currency = "EUR", stripeApiKey = ApiKey, stripeWebhookSecret = "whsec_x", sandbox = false }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await fixture.ScalarAsync<string>(
            $"SELECT \"Status\" FROM payment_configurations WHERE \"TenantId\" = '{tenantId}' AND \"Plane\" = 'Tenant'");
        status.ShouldBe("Active");

        var provider = await fixture.ScalarAsync<string>(
            $"SELECT \"Provider\" FROM payment_configurations WHERE \"TenantId\" = '{tenantId}' AND \"Plane\" = 'Tenant'");
        provider.ShouldBe("Stripe");

        // The stored bytes are the AES-GCM envelope (nonce+tag+ciphertext), NOT the 15-byte plaintext key.
        var sealedLength = await fixture.ScalarAsync<int>(
            $"SELECT octet_length(\"Ciphertext\") FROM tenant_secrets WHERE \"TenantId\" = '{tenantId}' AND \"Purpose\" = 'stripe.api_key'");
        sealedLength.ShouldBe(12 + 16 + ApiKey.Length);
        sealedLength.ShouldNotBe(ApiKey.Length, "the key must be stored sealed, never as plaintext");
    }

    [Fact]
    public async Task An_unknown_provider_is_rejected()
    {
        var (token, _) = await AdminAsync();

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", token,
            new { provider = "paypal", currency = "EUR", sandbox = false }));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
