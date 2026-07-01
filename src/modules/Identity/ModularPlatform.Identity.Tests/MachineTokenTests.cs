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
        var expiresAt = data.GetProperty("expiresAt").GetDateTimeOffset();

        var claims = DecodeJwt(machineToken);
        var machineSubjectId = Guid.Parse(claims.GetProperty("machine_id").GetString()!);
        var tokenId = claims.GetProperty("jti").GetString()!;
        claims.GetProperty("tenant_id").GetString().ShouldBe(tenantId.ToString());
        // The 'role' claim carries "machine" (single value serializes as a string).
        claims.GetProperty("role").GetString().ShouldBe("machine");
        claims.TryGetProperty("permission", out _).ShouldBeFalse();
        claims.GetProperty("sub").GetString().ShouldBe(machineSubjectId.ToString());
        claims.TryGetProperty("email", out _).ShouldBeFalse();
        claims.GetProperty("machine_name").GetString().ShouldBe("door-agent-1");
        expiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM users WHERE \"Id\" = '{machineSubjectId}'"))
            .ShouldBe(0);
        await fixture.WaitForCountAsync(
            "SELECT count(*)::bigint FROM machine_token_issuances " +
            $"WHERE \"MachineSubjectId\" = '{machineSubjectId}' AND \"TargetTenantId\" = '{tenantId}' " +
            $"AND \"TokenId\" = '{tokenId}' AND \"Name\" = 'door-agent-1'",
            1);
        await fixture.WaitForCountAsync(
            "SELECT count(*)::bigint FROM identity_audit_entries " +
            "WHERE \"EntityType\" = 'MachineTokenIssuance' AND \"Action\" = 'Create' " +
            $"AND \"EntityId\" = (SELECT \"Id\"::text FROM machine_token_issuances WHERE \"MachineSubjectId\" = '{machineSubjectId}')",
            1);

        var persistedValues = await fixture.ScalarAsync<string>(
            "SELECT coalesce(string_agg(row_to_json(i)::text, ''), '') FROM machine_token_issuances i " +
            $"WHERE \"MachineSubjectId\" = '{machineSubjectId}'");
        persistedValues.ShouldNotContain(machineToken);

        var auditValues = await fixture.ScalarAsync<string>(
            "SELECT coalesce(string_agg(\"NewValues\"::text, ''), '') FROM identity_audit_entries " +
            "WHERE \"EntityType\" = 'MachineTokenIssuance'");
        auditValues.ShouldNotContain(machineToken);

        var humanOnly = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/change-password", machineToken,
            new { currentPassword = "irrelevant", newPassword = "N3wSup3rSecret!" }));
        humanOnly.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var termsAcceptance = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/terms-acceptance", machineToken,
            new { termsVersion = "2026-07-01" }));
        termsAcceptance.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

    [Fact]
    public async Task Admin_lists_and_revokes_machine_tokens_without_exposing_raw_jwt()
    {
        var admin = await AdminTokenAsync();
        var tenantId = Guid.Parse(DecodeJwt(admin).GetProperty("tenant_id").GetString()!);

        var issue = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/admin/machine-tokens", admin,
            new { tenantId, name = "kiosk-agent-1" }));
        issue.StatusCode.ShouldBe(HttpStatusCode.OK);
        var issued = await PlatformApiFactory.ReadData(issue);
        var rawToken = issued.GetProperty("accessToken").GetString()!;
        var machineSubjectId = Guid.Parse(DecodeJwt(rawToken).GetProperty("machine_id").GetString()!);

        var beforeRevoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/me/entitlements", rawToken));
        beforeRevoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/machine-tokens?tenantId={tenantId}", admin));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray().ToArray();
        var item = items.Single(i => i.GetProperty("machineSubjectId").GetGuid() == machineSubjectId);
        var tokenRowId = item.GetProperty("id").GetGuid();
        item.GetProperty("name").GetString().ShouldBe("kiosk-agent-1");
        item.GetProperty("status").GetString().ShouldBe("Active");
        item.ToString().ShouldNotContain(rawToken);

        var revoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/machine-tokens/{tokenRowId}/revoke?tenantId={tenantId}", admin));
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revoked = await PlatformApiFactory.ReadData(revoke);
        revoked.GetProperty("id").GetGuid().ShouldBe(tokenRowId);
        revoked.GetProperty("status").GetString().ShouldBe("Revoked");
        revoked.GetProperty("revokedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var secondRevoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/machine-tokens/{tokenRowId}/revoke?tenantId={tenantId}", admin));
        secondRevoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(secondRevoke))
            .GetProperty("revokedAt").GetDateTimeOffset()
            .ShouldBe(revoked.GetProperty("revokedAt").GetDateTimeOffset());

        var afterRevoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/me/entitlements", rawToken));
        afterRevoke.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var afterList = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/machine-tokens?tenantId={tenantId}", admin));
        var afterItem = (await PlatformApiFactory.ReadData(afterList)).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == tokenRowId);
        afterItem.GetProperty("status").GetString().ShouldBe("Revoked");
    }
}
