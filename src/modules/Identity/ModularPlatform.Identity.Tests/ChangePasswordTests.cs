using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class ChangePasswordTests(PlatformApiFactory fixture)
{
    private const string OldPassword = "Sup3rSecret!";
    private const string NewPassword = "N3wSup3rSecret!";

    [Fact]
    public async Task Change_password_rejects_wrong_current_password()
    {
        var (_, accessToken, _) = await RegisterLoginWithTokensAsync(
            $"pwd-wrong-{Guid.CreateVersion7():N}@example.com", OldPassword);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/change-password",
            accessToken,
            new { currentPassword = "not-the-current-password", newPassword = NewPassword }));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldContain("user.current_password_invalid");
    }

    [Fact]
    public async Task Change_password_rejects_weak_new_password()
    {
        var (_, accessToken, _) = await RegisterLoginWithTokensAsync(
            $"pwd-weak-{Guid.CreateVersion7():N}@example.com", OldPassword);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/change-password",
            accessToken,
            new { currentPassword = OldPassword, newPassword = "short" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("user.password.too_short");
    }

    [Fact]
    public async Task Successful_password_change_revokes_existing_refresh_tokens_and_accepts_only_the_new_password()
    {
        var email = $"pwd-success-{Guid.CreateVersion7():N}@example.com";
        var (_, accessToken, refreshToken) = await RegisterLoginWithTokensAsync(email, OldPassword);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/change-password",
            accessToken,
            new { currentPassword = OldPassword, newPassword = NewPassword }));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var oldLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = OldPassword });
        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var newLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = NewPassword });
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Change_password_ignores_any_client_supplied_user_id()
    {
        var emailA = $"pwd-own-{Guid.CreateVersion7():N}@example.com";
        var emailB = $"pwd-other-{Guid.CreateVersion7():N}@example.com";
        var (_, accessA, _) = await RegisterLoginWithTokensAsync(emailA, OldPassword);
        var (userB, _, _) = await RegisterLoginWithTokensAsync(emailB, OldPassword);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/users/me/change-password?userId={userB}",
            accessA,
            new { currentPassword = OldPassword, newPassword = NewPassword }));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var userBLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = emailB, password = OldPassword });
        userBLogin.StatusCode.ShouldBe(HttpStatusCode.OK);

        var userAOldLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = emailA, password = OldPassword });
        userAOldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> RegisterLoginWithTokensAsync(
        string email,
        string password)
    {
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await PlatformApiFactory.ReadData(login);

        return (userId, tokens.GetProperty("accessToken").GetString()!, tokens.GetProperty("refreshToken").GetString()!);
    }
}
