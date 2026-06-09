using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Authorization model end-to-end: a permission-gated admin endpoint rejects a normal user (403), the configured
/// admin email is bootstrapped to the admin role on login and its token carries the permission claim, the admin
/// can grant a role to another user, and that user's next token carries the new permission (claims are a snapshot
/// refreshed on re-auth).
/// </summary>
[Collection("Integration")]
public sealed class AuthzTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Permission_gated_endpoint_rejects_non_admins_and_admins_can_grant_roles()
    {
        var normalEmail = $"user-{Guid.CreateVersion7():N}@x.com";
        var (normalId, normalToken) = await fixture.RegisterAndLoginAsync(normalEmail, Password);

        // A normal authenticated user is FORBIDDEN from the permission-gated admin endpoint.
        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/identity/admin/users/{normalId}/roles", normalToken, new { role = "admin" }));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The configured admin email gets the admin role on login; its token carries the manage-roles permission.
        var (_, adminToken) = await fixture.RegisterAndLoginAsync(PlatformApiFactory.AdminEmail, Password);
        ClaimValues(adminToken, "permission").ShouldContain("identity.manage_roles");
        ClaimValues(adminToken, "role").ShouldContain("admin");

        // The admin grants the normal user the admin role.
        var granted = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/identity/admin/users/{normalId}/roles", adminToken, new { role = "admin" }));
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // After re-authenticating, the normal user's NEW token carries the permission and the endpoint now allows.
        var reloginToken = await LoginAsync(normalEmail, Password);
        ClaimValues(reloginToken, "permission").ShouldContain("identity.manage_roles");

        var nowAllowed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/identity/admin/users/{normalId}/roles", reloginToken, new { role = "admin" }));
        nowAllowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var login = await fixture.Client.PostAsJsonAsync("/identity/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>All values of a (possibly multi-valued) JWT claim — JsonWebTokenHandler emits one value as a scalar, many as an array.</summary>
    private static IReadOnlyList<string> ClaimValues(string jwt, string claim)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        var json = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

        if (!json.TryGetProperty(claim, out var element))
        {
            return [];
        }

        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [element.GetString()!];
    }
}
