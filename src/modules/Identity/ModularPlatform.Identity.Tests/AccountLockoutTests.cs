using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// A13 — account lockout: after N consecutive wrong-password attempts the account is locked for a window,
/// and during that window even the CORRECT password is rejected with auth.locked_out.
/// </summary>
public sealed class AccountLockoutTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Password = "Sup3rSecret!";
    private const int Threshold = 5;

    [Fact]
    public async Task Locks_out_after_threshold_failures_and_rejects_correct_credentials()
    {
        var email = $"lockout-{Guid.CreateVersion7():N}@example.com";

        var register = await fixture.Client.PostAsJsonAsync("/identity/users",
            new { email, password = Password, displayName = "Lockout User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Sanity: correct credentials work before any failures.
        var preLogin = await fixture.Client.PostAsJsonAsync("/identity/auth/login",
            new { email, password = Password });
        preLogin.EnsureSuccessStatusCode();

        // Drive the account to the lockout threshold with wrong passwords.
        for (var attempt = 0; attempt < Threshold; attempt++)
        {
            var wrong = await fixture.Client.PostAsJsonAsync("/identity/auth/login",
                new { email, password = "wrong-password" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Now even the CORRECT password must be rejected while locked out.
        var lockedOut = await fixture.Client.PostAsJsonAsync("/identity/auth/login",
            new { email, password = Password });
        lockedOut.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problem = await lockedOut.Content.ReadAsStringAsync();
        problem.ShouldContain("auth.locked_out");
    }
}
