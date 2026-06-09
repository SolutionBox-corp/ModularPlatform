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
    private const string RlsRuntimeRole = "app_rls";
    private const string RlsRuntimePassword = "test_app_rls_pwd";

    /// <summary>Email configured as a platform admin — registering + logging in as this user grants the admin role.</summary>
    public const string AdminEmail = "admin@platform.test";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"mp-storage-{Guid.CreateVersion7():N}");
    private WebApplicationFactory<Program> _factory = default!;

    public HttpClient Client { get; private set; } = default!;
    public string ConnectionString => _postgres.GetConnectionString();
    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Messaging:SoloMode", "true");  // single-node -> Solo durability (drains immediately)
            builder.UseSetting("ConnectionStrings:Write", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Read", _postgres.GetConnectionString());
            builder.UseSetting("RunMigrationsAtStartup", "true");
            // RLS is ON by default; pin the runtime-role password so ScalarAsUserAsync can authenticate as it.
            builder.UseSetting("Persistence:Rls:RuntimePassword", RlsRuntimePassword);
            // Configure the well-known admin email so the authz tests can bootstrap an admin via login.
            builder.UseSetting("Identity:Auth:AdminEmails:0", AdminEmail);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32b");
            builder.UseSetting("Jwt:Issuer", "test");
            builder.UseSetting("Jwt:Audience", "test");
            // Functional tests share one loopback IP partition; raise the rate limits so they aren't throttled.
            // (The brute-force/rate-limit scenarios assert 429 via a dedicated low-limit host, not this one.)
            builder.UseSetting("RateLimiting:GlobalPermitsPerMinute", "100000");
            builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "100000");
            // Files module: use the local-disk storage provider with an isolated per-run temp root.
            builder.UseSetting("Storage:Provider", "local");
            builder.UseSetting("Storage:Local:RootPath", _storageRoot);
        });
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp storage root; ignore if files are still held.
        }
    }

    /// <summary>Registers a user, logs in, and returns the user id + a fresh access token.</summary>
    public async Task<(Guid UserId, string AccessToken)> RegisterAndLoginAsync(string email, string password)
    {
        var register = await Client.PostAsJsonAsync("/v1/identity/users", new { email, password });
        if (!register.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"register failed {(int)register.StatusCode}: {await register.Content.ReadAsStringAsync()}");
        }

        var userId = (await ReadData(register)).GetProperty("userId").GetGuid();

        var login = await Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password });
        if (!login.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"login failed {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
        }

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

    /// <summary>Polls a scalar count query until it reaches <paramref name="expected"/> (or times out).</summary>
    public async Task WaitForCountAsync(string countSql, long expected, int attempts = 100, int delayMs = 200)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (await ScalarAsync<long>(countSql) >= expected)
            {
                return;
            }

            await Task.Delay(delayMs);
        }

        throw new InvalidOperationException($"Condition not met in time: {countSql} >= {expected}");
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Runs a scalar query as the least-privilege RLS runtime role with the given user as the session principal
    /// (<c>app.is_system=off</c>). Unlike <see cref="ScalarAsync{T}"/> (which connects as the admin/superuser and
    /// bypasses RLS), this is subject to the row-level-security policies — use it to prove a user cannot see
    /// another user's rows even with a raw query.
    /// </summary>
    public async Task<T> ScalarAsUserAsync<T>(Guid principalUserId, string sql)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = RlsRuntimeRole,
            Password = RlsRuntimePassword,
        }.ConnectionString;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var setGucs = conn.CreateCommand())
        {
            setGucs.CommandText =
                "SELECT set_config('app.is_system', 'off', false), set_config('app.principal_id', @principal, false)";
            var p = setGucs.CreateParameter();
            p.ParameterName = "principal";
            p.Value = principalUserId.ToString();
            setGucs.Parameters.Add(p);
            await setGucs.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<JsonElement> ReadData(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("data");
    }
}
