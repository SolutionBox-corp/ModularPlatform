using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// The machine-principal seam: a platform-admin mints a tenant-scoped MACHINE token (for an edge agent / device
/// gateway). The token carries the target tenant's id (so it is tenant-scoped + cross-checked like a user token) and a
/// <c>machine</c> role, with no user identity. A non-admin cannot mint one.
/// </summary>
[Collection("Integration")]
public sealed class MachineTokenTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    private static JsonElement DecodeJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
    }

    [Fact]
    public async Task Admin_mints_a_tenant_scoped_machine_token()
    {
        var admin = await AdminTokenAsync();
        // The target tenant must exist — use the admin's own provisioned tenant (a real registry row).
        var tenantId = Guid.Parse(DecodeJwt(admin).GetProperty("tenant_id").GetString()!);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/admin/machine-tokens", admin,
            new { tenantId, name = "door-agent-1" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        var machineToken = data.GetProperty("accessToken").GetString()!;

        var claims = DecodeJwt(machineToken);
        claims.GetProperty("tenant_id").GetString().ShouldBe(tenantId.ToString());
        // The 'role' claim carries "machine" (single value serializes as a string).
        claims.GetProperty("role").GetString().ShouldBe("machine");
    }

    [Fact]
    public async Task A_non_admin_cannot_mint_a_machine_token()
    {
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"mach-{Guid.CreateVersion7():N}@x.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/admin/machine-tokens", userToken,
            new { tenantId = Guid.CreateVersion7(), name = "x" }));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
