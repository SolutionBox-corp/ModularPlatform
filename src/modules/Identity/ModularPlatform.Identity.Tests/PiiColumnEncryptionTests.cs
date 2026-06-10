using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// PII-at-rest column encryption (docs/pii-column-encryption-design.md):
/// <list type="bullet">
/// <item>users.Email/DisplayName are stored as <c>penc:v2:</c> envelopes — a DB dump contains no plaintext
/// address; lookups (login, duplicate check) go through the keyed blind index users.EmailHash.</item>
/// <item>The application still SEES plaintext: the model-level converter decrypts on read (profile).</item>
/// <item>GDPR erasure scrubs the row to a non-routable tombstone, blanks PasswordHash (login fails on
/// CREDENTIALS) and shreds the DEK — the previously stored ciphertext is unreadable forever.</item>
/// </list>
/// </summary>
[Collection("Integration")]
public sealed class PiiColumnEncryptionTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Email_and_display_name_are_ciphertext_at_rest_but_plaintext_through_the_api()
    {
        var email = $"pii-{Guid.CreateVersion7():N}@example.com";
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password, displayName = "Cipher Carol" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        // At rest: envelopes + the blind index, never the address.
        var storedEmail = await fixture.ScalarAsync<string>(
            $"SELECT \"Email\" FROM users WHERE \"Id\" = '{userId}'");
        storedEmail.ShouldStartWith("penc:v2:");
        storedEmail.ShouldNotContain(email);
        var storedName = await fixture.ScalarAsync<string>(
            $"SELECT \"DisplayName\" FROM users WHERE \"Id\" = '{userId}'");
        storedName.ShouldStartWith("penc:v2:");
        var storedHash = await fixture.ScalarAsync<string>(
            $"SELECT \"EmailHash\" FROM users WHERE \"Id\" = '{userId}'");
        storedHash.ShouldBe(PlatformApiFactory.EmailHashOf(email));

        // Pre-auth lookup via the blind index: login works...
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;

        // ...and reads decrypt transparently (write context AND the no-tracking read factory).
        var profile = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", token));
        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(profile);
        data.GetProperty("email").GetString().ShouldBe(email);
        data.GetProperty("displayName").GetString().ShouldBe("Cipher Carol");
    }

    [Fact]
    public async Task Duplicate_email_is_still_rejected_through_the_blind_index()
    {
        var email = $"dup-pii-{Guid.CreateVersion7():N}@example.com";
        (await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password })).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        // Different casing — normalization happens before hashing, so the index still collides.
        var second = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = email.ToUpperInvariant(), password = Password });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).ShouldContain("user.email_taken");
    }

    [Fact]
    public async Task Erasure_tombstones_the_row_blanks_the_password_and_kills_the_ciphertext()
    {
        var email = $"erase-pii-{Guid.CreateVersion7():N}@example.com";
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password, displayName = "Erase Eva" });
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email, password = Password });
        var token = (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;

        var erase = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", token));
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Worker drains the erasure fan-out: tombstone + blanked credentials + soft delete.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM users WHERE \"Id\" = '{userId}' AND \"DeletedAt\" IS NOT NULL", 1);
        var storedEmail = await fixture.ScalarAsync<string>(
            $"SELECT \"Email\" FROM users WHERE \"Id\" = '{userId}'");
        storedEmail.ShouldBe($"erased-{userId:N}@erased.invalid");
        var storedPassword = await fixture.ScalarAsync<string>(
            $"SELECT \"PasswordHash\" FROM users WHERE \"Id\" = '{userId}'");
        storedPassword.ShouldBe(string.Empty);

        // The DEK is shredded — even with the original credentials, login on the erased account fails.
        var reLogin = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email, password = Password });
        reLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the subject's key row is shredded (WrappedDek null) → old envelopes are unreadable forever.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subject_keys WHERE \"UserId\" = '{userId}' AND \"WrappedDek\" IS NULL", 1);
    }
}
