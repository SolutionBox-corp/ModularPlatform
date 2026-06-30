using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class ProfileUpdateTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Update_profile_normalizes_blank_display_name_and_persists_locale()
    {
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(
            $"profile-update-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, "/v1/identity/users/me",
            accessToken,
            new { displayName = "   ", locale = "cs" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("id").GetGuid().ShouldBe(userId);
        data.GetProperty("displayName").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        data.GetProperty("locale").GetString().ShouldBe("cs");
    }

    [Fact]
    public async Task Update_profile_rejects_unsupported_locale()
    {
        var (_, accessToken) = await fixture.RegisterAndLoginAsync(
            $"profile-locale-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, "/v1/identity/users/me",
            accessToken,
            new { displayName = "Locale User", locale = "de" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("user.locale.unsupported");
    }

    [Fact]
    public async Task Concurrent_profile_updates_are_serialized_without_server_errors()
    {
        var (_, accessToken) = await fixture.RegisterAndLoginAsync(
            $"profile-concurrent-{Guid.CreateVersion7():N}@example.com", Password);

        var first = fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, "/v1/identity/users/me",
            accessToken,
            new { displayName = "First Writer", locale = "en" }));
        var second = fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, "/v1/identity/users/me",
            accessToken,
            new { displayName = "Second Writer", locale = "cs" }));

        var responses = await Task.WhenAll(first, second);
        responses.ShouldAllBe(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(2);

        var profile = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", accessToken));
        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(profile);
        var displayName = data.GetProperty("displayName").GetString();
        displayName.ShouldBeOneOf("First Writer", "Second Writer");
    }

    [Fact]
    public async Task Update_profile_ignores_any_client_supplied_user_id()
    {
        var (userA, accessA) = await fixture.RegisterAndLoginAsync(
            $"profile-own-{Guid.CreateVersion7():N}@example.com", Password);
        var (userB, _) = await fixture.RegisterAndLoginAsync(
            $"profile-other-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, $"/v1/identity/users/me?userId={userB}",
            accessA,
            new { displayName = "Only Me", locale = "en" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("id").GetGuid().ShouldBe(userA);

        var otherUpdated = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM users WHERE \"Id\" = '{userB}' AND \"DisplayName\" IS NOT NULL");
        otherUpdated.ShouldBe(0);
    }

    [Fact]
    public async Task Update_profile_audit_row_records_changed_columns_only()
    {
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(
            $"profile-audit-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Patch, "/v1/identity/users/me",
            accessToken,
            new { displayName = "Audit Display Name", locale = "en" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var changedColumnsJson = await fixture.ScalarAsync<string>(
            "SELECT \"ChangedColumns\"::text FROM identity_audit_entries " +
            "WHERE \"Action\" = 'Update' AND \"EntityType\" = 'User' " +
            $"AND \"EntityId\" = '{userId}' ORDER BY \"Timestamp\" DESC LIMIT 1");
        var changedColumns = JsonSerializer.Deserialize<string[]>(changedColumnsJson)!;

        changedColumns.ShouldContain("DisplayName");
        changedColumns.ShouldNotContain("Email");
        changedColumns.ShouldNotContain("Locale");
        changedColumns.ShouldNotContain("PasswordHash");

        var newValuesJson = await fixture.ScalarAsync<string>(
            "SELECT \"NewValues\"::text FROM identity_audit_entries " +
            "WHERE \"Action\" = 'Update' AND \"EntityType\" = 'User' " +
            $"AND \"EntityId\" = '{userId}' ORDER BY \"Timestamp\" DESC LIMIT 1");
        using var values = JsonDocument.Parse(newValuesJson);

        values.RootElement.TryGetProperty("DisplayName", out var displayName).ShouldBeTrue();
        displayName.GetString().ShouldStartWith("penc:v2:");
        newValuesJson.ShouldNotContain("Audit Display Name");
        values.RootElement.TryGetProperty("Email", out _).ShouldBeFalse();
        values.RootElement.TryGetProperty("Locale", out _).ShouldBeFalse();
        values.RootElement.TryGetProperty("PasswordHash", out _).ShouldBeFalse();
    }
}
