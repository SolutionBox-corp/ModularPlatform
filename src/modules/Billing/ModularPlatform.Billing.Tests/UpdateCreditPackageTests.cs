using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC36: package updates are tenant-scoped admin mutations. They audit tracked changes, toggle public visibility and
/// do not rewrite already-started purchase snapshots.
/// </summary>
[Collection("Integration")]
public sealed class UpdateCreditPackageTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Update_package_requires_billing_manage_permission()
    {
        var adminToken = await EnsureAdminAsync();
        var packageId = await CreatePackageAsync(adminToken, $"UC36 forbidden {Guid.CreateVersion7():N}", 100, 5.00m, active: true);
        var (_, _, normalToken) = await RegisterUserAsync($"uc36-normal-{Guid.CreateVersion7():N}@example.test");

        var response = await UpdatePackageRawAsync(
            normalToken, packageId, $"UC36 forbidden update {Guid.CreateVersion7():N}", 200, 7.00m, active: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_package_returns_not_found_for_unknown_or_foreign_package()
    {
        var platformAdminToken = await EnsureAdminAsync();
        var packageId = await CreatePackageAsync(platformAdminToken, $"UC36 owned {Guid.CreateVersion7():N}", 100, 5.00m, active: true);

        var unknown = await UpdatePackageRawAsync(
            platformAdminToken, Guid.CreateVersion7(), $"UC36 unknown {Guid.CreateVersion7():N}", 100, 5.00m, active: true);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (tenantAdminId, tenantAdminEmail, _) = await RegisterUserAsync($"uc36-tenant-admin-{Guid.CreateVersion7():N}@example.test");
        await GrantAdminRoleAsync(platformAdminToken, tenantAdminId);
        var tenantAdminToken = await LoginAsync(tenantAdminEmail);

        var foreign = await UpdatePackageRawAsync(
            tenantAdminToken, packageId, $"UC36 foreign {Guid.CreateVersion7():N}", 100, 5.00m, active: true);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_package_can_disable_public_visibility_and_writes_audit_entry()
    {
        var adminToken = await EnsureAdminAsync();
        var packageId = await CreatePackageAsync(adminToken, $"UC36 disable {Guid.CreateVersion7():N}", 321, 12.00m, active: true);

        var before = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/packages", adminToken));
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(before)).EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == packageId)
            .ShouldBeTrue();

        var update = await UpdatePackageRawAsync(
            adminToken, packageId, $"UC36 disabled {Guid.CreateVersion7():N}", 654, 15.00m, active: false);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(update)).GetProperty("active").GetBoolean().ShouldBeFalse();

        var afterPublic = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/packages", adminToken));
        afterPublic.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(afterPublic)).EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == packageId)
            .ShouldBeFalse();

        var auditCount = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM billing_audit_entries WHERE \"EntityType\" = 'CreditPackage' AND \"EntityId\" = '{packageId}' AND \"Action\" = 'Update'");
        auditCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Update_package_does_not_rewrite_started_purchase_snapshot()
    {
        var adminToken = await EnsureAdminAsync();
        await ConfigureFakeGatewayAsync(adminToken);
        var packageId = await CreatePackageAsync(adminToken, $"UC36 snapshot {Guid.CreateVersion7():N}", 123, 10.00m, active: true);

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", adminToken));
        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        var purchaseId = (await PlatformApiFactory.ReadData(checkout)).GetProperty("purchaseId").GetGuid();

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}'", 1);

        var update = await UpdatePackageRawAsync(
            adminToken, packageId, $"UC36 snapshot changed {Guid.CreateVersion7():N}", 999, 99.00m, active: true);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var snapshotAmount = await fixture.ScalarAsync<long>(
            $"SELECT \"CreditAmount\" FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}'");
        snapshotAmount.ShouldBe(123);
    }

    [Fact]
    public async Task Concurrent_package_updates_are_serialized_without_server_errors()
    {
        var adminToken = await EnsureAdminAsync();
        var packageId = await CreatePackageAsync(adminToken, $"UC36 concurrent {Guid.CreateVersion7():N}", 100, 5.00m, active: true);

        var first = UpdatePackageRawAsync(
            adminToken, packageId, $"UC36 concurrent A {Guid.CreateVersion7():N}", 101, 6.00m, active: true);
        var second = UpdatePackageRawAsync(
            adminToken, packageId, $"UC36 concurrent B {Guid.CreateVersion7():N}", 102, 7.00m, active: false);

        var responses = await Task.WhenAll(first, second);
        responses.Select(r => r.StatusCode).ShouldAllBe(code => code == HttpStatusCode.OK);
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

    private Task<HttpResponseMessage> UpdatePackageRawAsync(
        string token, Guid packageId, string name, long creditAmount, decimal price, bool active) =>
        fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/billing/admin/packages/{packageId}", token,
            new
            {
                name,
                creditAmount,
                price,
                bucketExpiryDays = (int?)null,
                active,
                stripePriceId = $"price_{Guid.CreateVersion7():N}",
            }));

    private async Task<string> EnsureAdminAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        return await LoginAsync(PlatformApiFactory.AdminEmail);
    }

    private async Task<(Guid UserId, string Email, string AccessToken)> RegisterUserAsync(string email)
    {
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();
        return (userId, email, await LoginAsync(email));
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
