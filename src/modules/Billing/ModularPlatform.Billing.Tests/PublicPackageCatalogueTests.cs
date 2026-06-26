using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC33: public package catalogue is the Billing-owned source of truth for purchasable credit packages. Product/CRM
/// screens list these rows and then buy by returned id; they do not hardcode ids, prices or credit amounts.
/// </summary>
[Collection("Integration")]
public sealed class PublicPackageCatalogueTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Public_package_list_shows_only_active_packages_in_stable_order()
    {
        var adminToken = await EnsureAdminAsync();

        var inactiveId = await CreatePackageAsync(adminToken, $"ZZ inactive {Guid.CreateVersion7():N}", 100, 1.00m, active: false);
        var betaId = await CreatePackageAsync(adminToken, $"Beta same price {Guid.CreateVersion7():N}", 200, 5.00m, active: true);
        var alphaId = await CreatePackageAsync(adminToken, $"Alpha same price {Guid.CreateVersion7():N}", 300, 5.00m, active: true);
        var cheapId = await CreatePackageAsync(adminToken, $"Cheap {Guid.CreateVersion7():N}", 50, 1.00m, active: true);

        var list = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/packages", adminToken));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await PlatformApiFactory.ReadData(list)).EnumerateArray().ToList();

        items.Any(p => p.GetProperty("id").GetGuid() == inactiveId).ShouldBeFalse();

        var selected = items
            .Where(p => new[] { cheapId, alphaId, betaId }.Contains(p.GetProperty("id").GetGuid()))
            .Select(p => new
            {
                Id = p.GetProperty("id").GetGuid(),
                Name = p.GetProperty("name").GetString()!,
                Credits = p.GetProperty("creditAmount").GetInt64(),
                Price = p.GetProperty("price").GetDecimal(),
            })
            .ToList();

        selected.Select(p => p.Id).ShouldBe([cheapId, alphaId, betaId]);
        selected.Single(p => p.Id == alphaId).Credits.ShouldBe(300);
        selected.Single(p => p.Id == betaId).Price.ShouldBe(5.00m);
    }

    [Fact]
    public async Task Disabled_package_disappears_from_public_list_after_admin_update()
    {
        var adminToken = await EnsureAdminAsync();
        var packageId = await CreatePackageAsync(adminToken, $"Disable me {Guid.CreateVersion7():N}", 111, 3.00m, active: true);

        var before = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/packages", adminToken));
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(before)).EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == packageId)
            .ShouldBeTrue();

        var update = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/billing/admin/packages/{packageId}", adminToken,
            new
            {
                name = $"Disable me {Guid.CreateVersion7():N}",
                creditAmount = 111L,
                price = 3.00m,
                bucketExpiryDays = (int?)null,
                active = false,
                stripePriceId = "price_disabled",
            }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/packages", adminToken));
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(after)).EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == packageId)
            .ShouldBeFalse();
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
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue($"admin login failed: {(int)login.StatusCode}");
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
