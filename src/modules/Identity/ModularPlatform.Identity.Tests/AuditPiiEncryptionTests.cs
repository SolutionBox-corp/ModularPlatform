using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Audit-PII crypto-shred, end-to-end on the shared host (real Postgres, RLS on, Solo durability):
/// a user's PII (email/display name) is stored ENCRYPTED in <c>identity_audit_entries.NewValues</c> (never
/// plaintext); an admin can decrypt it via the forensic audit-trail endpoint; and after the subject erases
/// themselves (the GDPR fan-out shreds their DEK) the same PII is unrecoverable and surfaces as <c>[erased]</c>.
/// </summary>
[Collection("Integration")]
public sealed class AuditPiiEncryptionTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task User_pii_is_enveloped_in_audit_and_decryptable_by_an_admin()
    {
        var email = $"audit-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);

        // The create-audit row exists, and its NewValues JSON holds the email as a protected envelope — NOT plaintext.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM identity_audit_entries WHERE \"EntityType\" = 'User' " +
            $"AND \"EntityId\" = '{userId}' AND \"Action\" = 'Create'", 1);

        var rawNewValues = await fixture.ScalarAsync<string>(
            $"SELECT \"NewValues\"::text FROM identity_audit_entries WHERE \"EntityType\" = 'User' " +
            $"AND \"EntityId\" = '{userId}' AND \"Action\" = 'Create' LIMIT 1");
        rawNewValues.ShouldContain("penc:v2:");
        rawNewValues.ShouldNotContain(email);                    // plaintext email never hits the audit row
        rawNewValues.ShouldNotContain(email.ToUpperInvariant()); // nor the normalized form

        // A tenant admin in the same tenant reveals the real values through the forensic endpoint.
        var auditToken = await TenantAuditTokenAsync(userId, email);
        var create = await GetCreateAuditEntryAsync(auditToken, userId);
        create.GetProperty("values").GetProperty("Email").GetString().ShouldBe(email);
    }

    [Fact]
    public async Task Erasing_the_subject_makes_audit_pii_unrecoverable()
    {
        var email = $"erase-{Guid.CreateVersion7():N}@example.com";
        var (userId, userToken) = await fixture.RegisterAndLoginAsync(email, Password);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subject_keys WHERE \"UserId\" = '{userId}' AND \"WrappedDek\" IS NOT NULL", 1);

        // Pre-erasure: a same-tenant audit admin can still decrypt.
        var auditToken = await TenantAuditTokenAsync(userId, email);
        (await GetCreateAuditEntryAsync(auditToken, userId))
            .GetProperty("values").GetProperty("Email").GetString().ShouldBe(email);

        // The subject erases themselves; the GDPR fan-out shreds the DEK (WrappedDek -> null).
        var erase = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", userToken));
        erase.EnsureSuccessStatusCode();
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subject_keys WHERE \"UserId\" = '{userId}' AND \"WrappedDek\" IS NULL", 1);

        // Post-erasure: the envelope can no longer be decrypted -> surfaced as [erased]; the raw row still has no plaintext.
        var create = await GetCreateAuditEntryAsync(auditToken, userId);
        create.GetProperty("values").GetProperty("Email").GetString().ShouldBe("[erased]");

        var rawNewValues = await fixture.ScalarAsync<string>(
            $"SELECT \"NewValues\"::text FROM identity_audit_entries WHERE \"EntityType\" = 'User' " +
            $"AND \"EntityId\" = '{userId}' AND \"Action\" = 'Create' LIMIT 1");
        rawNewValues.ShouldNotContain(email);
    }

    [Fact]
    public async Task Tenant_audit_requires_permission_and_does_not_cross_tenant()
    {
        var tenantAdminEmail = $"audit-tenant-admin-{Guid.CreateVersion7():N}@example.com";
        var otherEmail = $"audit-other-{Guid.CreateVersion7():N}@example.com";
        var (tenantAdminId, normalToken) = await fixture.RegisterAndLoginAsync(tenantAdminEmail, Password);
        var (otherUserId, _) = await fixture.RegisterAndLoginAsync(otherEmail, Password);

        var forbidden = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/identity/admin/users/{tenantAdminId}/audit", normalToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tenantAuditToken = await TenantAuditTokenAsync(tenantAdminId, tenantAdminEmail);
        var crossTenant = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/identity/admin/users/{otherUserId}/audit", tenantAuditToken));
        crossTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var platformAudit = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/identity/platform/users/{otherUserId}/audit", await AdminTokenAsync()));
        platformAudit.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Fetches the user's tenant-scoped audit trail and returns the single Create entry.</summary>
    private async Task<JsonElement> GetCreateAuditEntryAsync(string adminToken, Guid userId)
    {
        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/identity/admin/users/{userId}/audit", adminToken));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        return data.GetProperty("entries").EnumerateArray()
            .First(e => e.GetProperty("action").GetString() == "Create");
    }

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

    private async Task<string> TenantAuditTokenAsync(Guid userId, string email)
    {
        var grant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/identity/admin/users/{userId}/roles",
            await AdminTokenAsync(),
            new { role = "admin" }));
        grant.StatusCode.ShouldBe(HttpStatusCode.OK);

        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
