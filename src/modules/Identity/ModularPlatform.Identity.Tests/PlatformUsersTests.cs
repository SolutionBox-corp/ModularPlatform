using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class PlatformUsersTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Platform_user_list_requires_permission_and_returns_limited_page()
    {
        var (_, normalToken) = await fixture.RegisterAndLoginAsync(
            $"platform-list-normal-{Guid.CreateVersion7():N}@example.com", Password);
        await fixture.RegisterAndLoginAsync($"platform-list-a-{Guid.CreateVersion7():N}@example.com", Password);
        await fixture.RegisterAndLoginAsync($"platform-list-b-{Guid.CreateVersion7():N}@example.com", Password);

        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/identity/platform/users", normalToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/identity/platform/users?limit=2&offset=0", await AdminTokenAsync()));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("limit").GetInt32().ShouldBe(2);
        data.GetProperty("offset").GetInt32().ShouldBe(0);
        data.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(3);
        data.GetProperty("items").EnumerateArray().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Platform_user_list_filters_by_tenant_and_hides_soft_deleted_users()
    {
        var (tenantUserId, _) = await fixture.RegisterAndLoginAsync(
            $"platform-list-tenant-{Guid.CreateVersion7():N}@example.com", Password);
        var (otherUserId, _) = await fixture.RegisterAndLoginAsync(
            $"platform-list-other-{Guid.CreateVersion7():N}@example.com", Password);
        var (deletedUserId, _) = await fixture.RegisterAndLoginAsync(
            $"platform-list-deleted-{Guid.CreateVersion7():N}@example.com", Password);

        var tenantId = await TenantIdOf(tenantUserId);
        var deletedTenantId = await TenantIdOf(deletedUserId);
        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"DeletedAt\" = now() WHERE \"Id\" = '{deletedUserId}'");

        var filtered = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get,
            $"/v1/identity/platform/users?tenantId={tenantId}&limit=50&offset=0",
            await AdminTokenAsync()));
        filtered.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = (await PlatformApiFactory.ReadData(filtered))
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        items.ShouldNotBeEmpty();
        items.ShouldAllBe(item => item.GetProperty("tenantId").GetGuid() == tenantId);
        items.Select(UserId).ShouldContain(tenantUserId);
        items.Select(UserId).ShouldNotContain(otherUserId);

        var deletedTenant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get,
            $"/v1/identity/platform/users?tenantId={deletedTenantId}&limit=50&offset=0",
            await AdminTokenAsync()));
        deletedTenant.StatusCode.ShouldBe(HttpStatusCode.OK);

        var deletedItems = (await PlatformApiFactory.ReadData(deletedTenant))
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        deletedItems.Select(UserId).ShouldNotContain(deletedUserId);
    }

    [Fact]
    public async Task Platform_user_list_can_find_a_user_by_exact_email_blind_index()
    {
        var targetEmail = $"platform-search-{Guid.CreateVersion7():N}@example.com";
        var (targetUserId, _) = await fixture.RegisterAndLoginAsync(targetEmail, Password);
        await fixture.RegisterAndLoginAsync($"platform-search-other-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get,
            $"/v1/identity/platform/users?email={Uri.EscapeDataString("  " + targetEmail.ToUpperInvariant() + "  ")}",
            await AdminTokenAsync()));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("total").GetInt32().ShouldBe(1);
        data.GetProperty("items").EnumerateArray().Select(UserId).ShouldBe([targetUserId]);
    }

    [Fact]
    public async Task Platform_user_list_orders_created_at_ties_by_user_id_for_stable_paging()
    {
        var (firstUserId, _) = await fixture.RegisterAndLoginAsync(
            $"platform-list-stable-1-{Guid.CreateVersion7():N}@example.com", Password);
        var (secondUserId, _) = await fixture.RegisterAndLoginAsync(
            $"platform-list-stable-2-{Guid.CreateVersion7():N}@example.com", Password);
        var (thirdUserId, _) = await fixture.RegisterAndLoginAsync(
            $"platform-list-stable-3-{Guid.CreateVersion7():N}@example.com", Password);

        await fixture.ExecuteSqlAsync(
            "UPDATE users " +
            "SET \"CreatedAt\" = timestamp with time zone '2030-01-01 00:00:00+00' " +
            $"WHERE \"Id\" IN ('{firstUserId}', '{secondUserId}', '{thirdUserId}')");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/identity/platform/users?limit=3&offset=0", await AdminTokenAsync()));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(response)).GetProperty("items").EnumerateArray()
            .Select(UserId)
            .ShouldBe(new[] { firstUserId, secondUserId, thirdUserId }.OrderByDescending(id => id).ToArray());
    }

    private async Task<Guid> TenantIdOf(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");

    private static Guid UserId(JsonElement item) => item.GetProperty("userId").GetGuid();

    private async Task<string> AdminTokenAsync()
    {
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        register.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict);

        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.EnsureSuccessStatusCode();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
