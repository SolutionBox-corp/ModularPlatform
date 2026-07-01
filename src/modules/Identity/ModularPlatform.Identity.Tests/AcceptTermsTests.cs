using System.Net;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class AcceptTermsTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Accept_terms_records_version_timestamp_and_returns_profile()
    {
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(
            $"terms-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            "/v1/identity/users/me/terms-acceptance",
            accessToken,
            new { termsVersion = "2026-07-01" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("id").GetGuid().ShouldBe(userId);
        data.GetProperty("acceptedTermsVersion").GetString().ShouldBe("2026-07-01");
        data.GetProperty("acceptedTermsAt").GetDateTimeOffset().ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);

        var profile = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", accessToken));
        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        var profileData = await PlatformApiFactory.ReadData(profile);
        profileData.GetProperty("acceptedTermsVersion").GetString().ShouldBe("2026-07-01");
        profileData.GetProperty("acceptedTermsAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Accept_terms_trims_version_and_audits_only_terms_columns()
    {
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(
            $"terms-audit-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            "/v1/identity/users/me/terms-acceptance",
            accessToken,
            new { termsVersion = "  terms-2026-07  " }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("acceptedTermsVersion").GetString().ShouldBe("terms-2026-07");

        var changedColumnsJson = await fixture.ScalarAsync<string>(
            "SELECT \"ChangedColumns\"::text FROM identity_audit_entries " +
            "WHERE \"Action\" = 'Update' AND \"EntityType\" = 'User' " +
            $"AND \"EntityId\" = '{userId}' ORDER BY \"Timestamp\" DESC LIMIT 1");
        var changedColumns = JsonSerializer.Deserialize<string[]>(changedColumnsJson)!;

        changedColumns.ShouldContain("AcceptedTermsVersion");
        changedColumns.ShouldContain("AcceptedTermsAt");
        changedColumns.ShouldNotContain("Email");
        changedColumns.ShouldNotContain("PasswordHash");
    }

    [Theory]
    [InlineData("", "user.accepted_terms_version.required")]
    [InlineData("                                 ", "user.accepted_terms_version.too_long")]
    public async Task Accept_terms_validates_terms_version(string termsVersion, string errorCode)
    {
        var (_, accessToken) = await fixture.RegisterAndLoginAsync(
            $"terms-validation-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            "/v1/identity/users/me/terms-acceptance",
            accessToken,
            new { termsVersion }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(errorCode);
    }

    [Fact]
    public async Task Accept_terms_ignores_any_client_supplied_user_id()
    {
        var (userA, accessA) = await fixture.RegisterAndLoginAsync(
            $"terms-own-{Guid.CreateVersion7():N}@example.com", Password);
        var (userB, _) = await fixture.RegisterAndLoginAsync(
            $"terms-other-{Guid.CreateVersion7():N}@example.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/identity/users/me/terms-acceptance?userId={userB}",
            accessA,
            new { termsVersion = "2026-07-01" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("id").GetGuid().ShouldBe(userA);

        var otherAccepted = await fixture.ScalarAsync<long>(
            "SELECT count(*)::bigint FROM users " +
            $"WHERE \"Id\" = '{userB}' AND \"AcceptedTermsVersion\" IS NOT NULL");
        otherAccepted.ShouldBe(0);
    }
}
