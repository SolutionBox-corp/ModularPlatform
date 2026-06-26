using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC37: package checkout is only the accept step. It creates a provider checkout + pending saga; credits are granted
/// later by the confirmed webhook path, never by the initial button click.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseCreditPackageTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Package_checkout_rejects_missing_or_disabled_package_before_provider_call()
    {
        var adminToken = await EnsureAdminAsync();

        var missing = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{Guid.CreateVersion7()}/checkout", adminToken));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("billing.package_not_found");

        var disabledPackageId = await CreatePackageAsync(
            adminToken, $"UC37 disabled {Guid.CreateVersion7():N}", 100, 3.00m, active: false);
        var disabled = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{disabledPackageId}/checkout", adminToken));
        disabled.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await disabled.Content.ReadAsStringAsync()).ShouldContain("billing.package_inactive");
    }

    [Fact]
    public async Task Package_checkout_fails_when_tenant_gateway_is_not_configured()
    {
        var platformAdminToken = await EnsureAdminAsync();
        var (tenantAdminId, tenantAdminEmail) = await RegisterUserAsync($"uc37-no-gateway-{Guid.CreateVersion7():N}@example.test");
        await GrantAdminRoleAsync(platformAdminToken, tenantAdminId);
        var tenantAdminToken = await LoginAsync(tenantAdminEmail);
        var packageId = await CreatePackageAsync(
            tenantAdminToken, $"UC37 no gateway {Guid.CreateVersion7():N}", 100, 3.00m, active: true);

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", tenantAdminToken));

        checkout.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await checkout.Content.ReadAsStringAsync()).ShouldContain("payment.gateway_not_configured");
    }

    [Fact]
    public async Task Duplicate_checkout_clicks_create_separate_pending_purchases_and_do_not_grant_credits()
    {
        var adminToken = await EnsureAdminAsync();
        await ConfigureFakeGatewayAsync(adminToken);
        var packageId = await CreatePackageAsync(
            adminToken, $"UC37 duplicate click {Guid.CreateVersion7():N}", 222, 8.00m, active: true);

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", adminToken));
        var second = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", adminToken));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstPurchaseId = (await PlatformApiFactory.ReadData(first)).GetProperty("purchaseId").GetGuid();
        var secondPurchaseId = (await PlatformApiFactory.ReadData(second)).GetProperty("purchaseId").GetGuid();
        secondPurchaseId.ShouldNotBe(firstPurchaseId);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" IN ('{firstPurchaseId}', '{secondPurchaseId}') AND \"Status\" = 'Pending'", 2);

        var grants = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" IN ('purchase:{firstPurchaseId}', 'purchase:{secondPurchaseId}')");
        grants.ShouldBe(0);
    }

    private async Task ConfigureFakeGatewayAsync(string adminToken)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", adminToken,
            new { provider = "fake", currency = "EUR", sandbox = false }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreatePackageAsync(string token, string name, long creditAmount, decimal price, bool active)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/admin/packages", token,
            new
            {
                name,
                creditAmount,
                price,
                currency = "EUR",
                bucketExpiryDays = (int?)null,
                active,
                stripePriceId = $"price_{Guid.CreateVersion7():N}",
            }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(response)).GetProperty("id").GetGuid();
    }

    private async Task<string> EnsureAdminAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        return await LoginAsync(PlatformApiFactory.AdminEmail);
    }

    private async Task<(Guid UserId, string Email)> RegisterUserAsync(string email)
    {
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();
        return (userId, email);
    }

    private async Task<string> LoginAsync(string email)
    {
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    private async Task GrantAdminRoleAsync(string platformAdminToken, Guid userId)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", platformAdminToken, new { role = "admin" }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
