using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC34: admin package catalogue is the management view. It is permission-gated, paged and includes disabled rows;
/// the public catalogue remains a separate purchasable-only view.
/// </summary>
[Collection("Integration")]
public sealed class AdminPackageCatalogueTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Admin_package_list_requires_billing_manage_permission()
    {
        var token = await RegisterAndLoginAsync($"crm-user-{Guid.CreateVersion7():N}@example.test");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/admin/packages?page=1&pageSize=10", token));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_package_list_includes_disabled_packages_and_returns_paged_stable_order()
    {
        var adminToken = await EnsureAdminAsync();
        var suffix = Guid.CreateVersion7().ToString("N");

        var gammaId = await CreatePackageAsync(adminToken, $"UC34 Gamma {suffix}", 300, 123.45m, active: true);
        var alphaId = await CreatePackageAsync(adminToken, $"UC34 Alpha {suffix}", 100, 123.45m, active: false);
        var betaId = await CreatePackageAsync(adminToken, $"UC34 Beta {suffix}", 200, 123.45m, active: true);

        var firstPage = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/admin/packages?page=1&pageSize=2", adminToken));
        firstPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await PlatformApiFactory.ReadData(firstPage);
        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(2);
        page.GetProperty("items").GetArrayLength().ShouldBeLessThanOrEqualTo(2);
        page.GetProperty("totalCount").GetInt64().ShouldBeGreaterThanOrEqualTo(3);
        page.GetProperty("totalPages").GetInt32().ShouldBeGreaterThanOrEqualTo(2);

        var all = await ReadAdminPackagesAsync(adminToken);
        all.Any(p => p.Id == alphaId && !p.Active).ShouldBeTrue();

        var selected = all
            .Where(p => p.Id == alphaId || p.Id == betaId || p.Id == gammaId)
            .Select(p => p.Id)
            .ToList();

        selected.ShouldBe([alphaId, betaId, gammaId]);
    }

    [Fact]
    public async Task Admin_and_public_package_lists_are_separate_views()
    {
        var adminToken = await EnsureAdminAsync();
        var disabledId = await CreatePackageAsync(
            adminToken, $"UC34 disabled {Guid.CreateVersion7():N}", 700, 44.00m, active: false);

        var adminItems = await ReadAdminPackagesAsync(adminToken);
        adminItems.Any(p => p.Id == disabledId && !p.Active).ShouldBeTrue();

        var publicList = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/packages", adminToken));
        publicList.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(publicList)).EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == disabledId)
            .ShouldBeFalse();
    }

    private async Task<IReadOnlyList<AdminPackageRow>> ReadAdminPackagesAsync(string adminToken)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/admin/packages?page=1&pageSize=100", adminToken));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        return data.GetProperty("items").EnumerateArray().Select(ToRow).ToList();
    }

    private static AdminPackageRow ToRow(JsonElement item) => new(
        item.GetProperty("id").GetGuid(),
        item.GetProperty("name").GetString()!,
        item.GetProperty("price").GetDecimal(),
        item.GetProperty("active").GetBoolean());

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

    private async Task<string> RegisterAndLoginAsync(string email)
    {
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    private sealed record AdminPackageRow(Guid Id, string Name, decimal Price, bool Active);
}
