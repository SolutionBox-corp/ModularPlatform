using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ModularPlatform.IntegrationTesting;

/// <summary>
/// Shared integration-test host: a real Postgres (Testcontainers) + the full Api host (all modules) booted via
/// <see cref="WebApplicationFactory{T}"/> with migrations applied at startup. Reuse this for every module's
/// integration tests so they exercise the real dispatcher, DbContexts, audit interceptor and Wolverine wiring.
/// </summary>
public sealed class PlatformApiFactory : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private WebApplicationFactory<Program> _factory = default!;

    public HttpClient Client { get; private set; } = default!;
    public string ConnectionString => _postgres.GetConnectionString();
    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Write", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Read", _postgres.GetConnectionString());
            builder.UseSetting("RunMigrationsAtStartup", "true");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32b");
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

    /// <summary>Registers a user, logs in, and returns the user id + a fresh access token.</summary>
    public async Task<(Guid UserId, string AccessToken)> RegisterAndLoginAsync(string email, string password)
    {
        var register = await Client.PostAsJsonAsync("/identity/users", new { email, password });
        register.EnsureSuccessStatusCode();
        var userId = (await ReadData(register)).GetProperty("userId").GetGuid();

        var login = await Client.PostAsJsonAsync("/identity/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var accessToken = (await ReadData(login)).GetProperty("accessToken").GetString()!;

        return (userId, accessToken);
    }

    /// <summary>An HttpClient request message carrying the bearer token.</summary>
    public HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    public async Task ExecuteSqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<JsonElement> ReadData(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("data");
    }
}
