using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// A13 — account lockout: after N consecutive wrong-password attempts the account is locked for a window,
/// and during that window even the CORRECT password is rejected with auth.locked_out.
/// </summary>
[Collection("Integration")]
public sealed class AccountLockoutTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";
    private const int Threshold = 5;

    [Fact]
    public async Task Locks_out_after_threshold_failures_and_rejects_correct_credentials()
    {
        var email = $"lockout-{Guid.CreateVersion7():N}@example.com";

        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = Password, displayName = "Lockout User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Sanity: correct credentials work before any failures.
        var preLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = Password });
        preLogin.EnsureSuccessStatusCode();

        // Drive the account to the lockout threshold with wrong passwords.
        for (var attempt = 0; attempt < Threshold; attempt++)
        {
            var wrong = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
                new { email, password = "wrong-password" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Now even the CORRECT password must be rejected while locked out.
        var lockedOut = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = Password });
        lockedOut.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problem = await lockedOut.Content.ReadAsStringAsync();
        problem.ShouldContain("auth.locked_out");
    }

    [Fact]
    public async Task Lockout_expires_after_the_window_and_the_correct_password_works_again()
    {
        var email = $"lockexpire-{Guid.CreateVersion7():N}@example.com";
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        for (var attempt = 0; attempt < Threshold; attempt++)
        {
            (await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
                new { email, password = "wrong-password" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The account is locked. Backdate the window into the past instead of waiting out the real 15 minutes.
        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"LockoutEndUtc\" = now() - interval '1 minute' WHERE \"Id\" = '{userId}'");

        // Once the window has lapsed, the correct password works again.
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_successful_login_resets_the_failed_attempt_counter()
    {
        var email = $"lockreset-{Guid.CreateVersion7():N}@example.com";
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        // A few wrong attempts, staying BELOW the lockout threshold.
        for (var attempt = 0; attempt < Threshold - 1; attempt++)
        {
            (await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
                new { email, password = "wrong-password" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // A correct login resets the counter, so a later wrong attempt starts from zero (no carry-over).
        (await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = Password })).EnsureSuccessStatusCode();

        (await fixture.ScalarAsync<int>(
            $"SELECT \"FailedAccessCount\" FROM users WHERE \"Id\" = '{userId}'")).ShouldBe(0);
    }
}
