using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.IntegrationTesting;
using Shouldly;
using Wolverine;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC38: purchase status is the user-facing read model over the persisted saga row. The frontend polls this endpoint;
/// other modules must not read Billing tables directly.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseStatusTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Purchase_status_is_owner_scoped_and_moves_through_pending_abandoned_completed()
    {
        var adminToken = await EnsureAdminAsync();
        await ConfigureFakeGatewayAsync(adminToken);
        var userId = await UserIdByEmailAsync(PlatformApiFactory.AdminEmail);
        var packageId = await CreatePackageAsync(adminToken, $"UC38 package {Guid.CreateVersion7():N}", 333, 8.00m);

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", adminToken));
        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        var purchaseId = (await PlatformApiFactory.ReadData(checkout)).GetProperty("purchaseId").GetGuid();

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Pending'", 1);

        var pending = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/billing/purchases/{purchaseId}", adminToken));
        pending.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(pending)).GetProperty("status").GetString().ShouldBe("Pending");

        var (_, foreignToken) = await fixture.RegisterAndLoginAsync(
            $"uc38-foreign-{Guid.CreateVersion7():N}@example.test", Password);
        var foreign = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/billing/purchases/{purchaseId}", foreignToken));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await PublishAsync(new CreditPurchaseTimeout(purchaseId, Minutes: 0));
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Abandoned'", 1);

        var abandoned = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/billing/purchases/{purchaseId}", adminToken));
        abandoned.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(abandoned)).GetProperty("status").GetString().ShouldBe("Abandoned");

        await PublishAsync(new CreditPurchaseConfirmed(
            purchaseId, userId, CreditAmount: 333, BucketExpiryDays: null, StripeEventId: $"evt_{Guid.CreateVersion7():N}"));
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Completed'", 1);

        var completed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/billing/purchases/{purchaseId}", adminToken));
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var completedData = await PlatformApiFactory.ReadData(completed);
        completedData.GetProperty("status").GetString().ShouldBe("Completed");
        completedData.GetProperty("resolvedAt").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
    }

    private async Task ConfigureFakeGatewayAsync(string adminToken)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", adminToken,
            new { provider = "fake", currency = "EUR", sandbox = false }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreatePackageAsync(string token, string name, long creditAmount, decimal price)
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
                active = true,
                stripePriceId = $"price_{Guid.CreateVersion7():N}",
            }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(response)).GetProperty("id").GetGuid();
    }

    private async Task<string> EnsureAdminAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    private Task<Guid> UserIdByEmailAsync(string email) =>
        fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM users WHERE \"EmailHash\" = '{PlatformApiFactory.EmailHashOf(email)}'");

    private async Task PublishAsync(object message)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(message);
    }
}
