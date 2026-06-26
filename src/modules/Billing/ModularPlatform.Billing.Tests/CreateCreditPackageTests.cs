using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC35: package creation is an admin catalogue mutation. Validation protects money-like values and ISO currency
/// shape; duplicate package names are rejected inside the caller's catalogue; active=false creates a disabled row.
/// </summary>
[Collection("Integration")]
public sealed class CreateCreditPackageTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Create_package_validates_amount_price_and_currency()
    {
        var adminToken = await EnsureAdminAsync();

        var zeroCredits = await CreatePackageRawAsync(adminToken, new
        {
            name = $"UC35 zero {Guid.CreateVersion7():N}",
            creditAmount = 0L,
            price = 1.00m,
            currency = "EUR",
            bucketExpiryDays = (int?)null,
            active = true,
            stripePriceId = "price_zero",
        });
        zeroCredits.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await zeroCredits.Content.ReadAsStringAsync()).ShouldContain("billing.package.credit_amount.must_be_positive");

        var negativePrice = await CreatePackageRawAsync(adminToken, new
        {
            name = $"UC35 negative {Guid.CreateVersion7():N}",
            creditAmount = 100L,
            price = -0.01m,
            currency = "EUR",
            bucketExpiryDays = (int?)null,
            active = true,
            stripePriceId = "price_negative",
        });
        negativePrice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await negativePrice.Content.ReadAsStringAsync()).ShouldContain("billing.package.price.must_not_be_negative");

        var invalidCurrency = await CreatePackageRawAsync(adminToken, new
        {
            name = $"UC35 currency {Guid.CreateVersion7():N}",
            creditAmount = 100L,
            price = 1.00m,
            currency = "EU1",
            bucketExpiryDays = (int?)null,
            active = true,
            stripePriceId = "price_currency",
        });
        invalidCurrency.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalidCurrency.Content.ReadAsStringAsync()).ShouldContain("billing.package.currency.invalid");
    }

    [Fact]
    public async Task Create_package_rejects_duplicate_name_in_admin_catalogue()
    {
        var adminToken = await EnsureAdminAsync();
        var name = $"UC35 duplicate {Guid.CreateVersion7():N}";

        var first = await CreatePackageRawAsync(adminToken, ValidPackage(name, active: true));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var duplicate = await CreatePackageRawAsync(adminToken, ValidPackage(name, active: false));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).ShouldContain("billing.package.name_taken");
    }

    [Fact]
    public async Task Create_package_respects_active_flag_and_new_active_package_is_visible_in_lists()
    {
        var adminToken = await EnsureAdminAsync();
        var disabledName = $"UC35 disabled {Guid.CreateVersion7():N}";
        var activeName = $"UC35 active {Guid.CreateVersion7():N}";

        var disabledId = await CreatePackageAsync(adminToken, ValidPackage(disabledName, active: false));
        var activeId = await CreatePackageAsync(adminToken, ValidPackage(activeName, active: true));

        var adminItems = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/admin/packages?page=1&pageSize=100", adminToken));
        adminItems.StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminRows = (await PlatformApiFactory.ReadData(adminItems)).GetProperty("items").EnumerateArray().ToList();
        adminRows.Any(p => p.GetProperty("id").GetGuid() == disabledId && !p.GetProperty("active").GetBoolean())
            .ShouldBeTrue();
        adminRows.Any(p => p.GetProperty("id").GetGuid() == activeId && p.GetProperty("active").GetBoolean())
            .ShouldBeTrue();

        var publicItems = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/packages", adminToken));
        publicItems.StatusCode.ShouldBe(HttpStatusCode.OK);
        var publicRows = (await PlatformApiFactory.ReadData(publicItems)).EnumerateArray().ToList();
        publicRows.Any(p => p.GetProperty("id").GetGuid() == disabledId).ShouldBeFalse();
        publicRows.Any(p => p.GetProperty("id").GetGuid() == activeId).ShouldBeTrue();
    }

    private async Task<Guid> CreatePackageAsync(string token, object payload)
    {
        var response = await CreatePackageRawAsync(token, payload);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> CreatePackageRawAsync(string token, object payload) =>
        fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/billing/admin/packages", token, payload));

    private static object ValidPackage(string name, bool active) => new
    {
        name,
        creditAmount = 250L,
        price = 9.99m,
        currency = "eur",
        bucketExpiryDays = (int?)null,
        active,
        stripePriceId = $"price_{Guid.CreateVersion7():N}",
    };

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
