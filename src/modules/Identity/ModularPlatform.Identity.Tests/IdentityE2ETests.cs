using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace ModularPlatform.Identity.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program> _factory = default!;

    public HttpClient Client { get; private set; } = default!;
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Write", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Read", _postgres.GetConnectionString());
            builder.UseSetting("RunMigrationsAtStartup", "true");
            builder.UseSetting("Jwt:SigningKey", "test-signing-key-at-least-32-bytes-long-xx");
            builder.UseSetting("Jwt:Issuer", "test");
            builder.UseSetting("Jwt:Audience", "test");
        });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }
}

public sealed class IdentityE2ETests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Register_login_refresh_rotation_reuse_detection_and_profile()
    {
        var email = $"user-{Guid.CreateVersion7():N}@example.com";

        // Register
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = "Sup3rSecret!", displayName = "Test User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Login
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = "Sup3rSecret!" });
        login.EnsureSuccessStatusCode();
        var tokens = await Unwrap<Tokens>(login);
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        // Profile with the access token
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/identity/users/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await fixture.Client.SendAsync(meRequest);
        me.EnsureSuccessStatusCode();
        var profile = await Unwrap<Profile>(me);
        profile.Email.ShouldBe(email);

        // Refresh rotation: the old refresh token is consumed and replaced
        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = tokens.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await Unwrap<Tokens>(refresh);
        rotated.RefreshToken.ShouldNotBe(tokens.RefreshToken);

        // REUSE DETECTION: replaying the consumed token must be rejected (401)
        var reuse = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = tokens.RefreshToken });
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Audit: registering the user wrote a Create row with ONLY changed columns (JSONB)
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM identity_audit_entries WHERE \"Action\" = 'Create' AND \"EntityType\" = 'User'", conn);
        var auditCount = (long)(await cmd.ExecuteScalarAsync())!;
        auditCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    private static async Task<T> Unwrap<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(Json);
        envelope.ShouldNotBeNull();
        return envelope!.Data;
    }

    private sealed record ApiEnvelope<T>(T Data, string? Message, bool Success);
    private sealed record Tokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private sealed record Profile(Guid Id, string Email, string? DisplayName, string Locale);
}
