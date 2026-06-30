using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Rls;
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
    // HARD INVARIANT: ONE host (and ONE Postgres) per test process. The personal-data decrypting converter
    // reads a process-wide protector (PersonalDataEncryption.Protector — converters live in EF's cached model
    // and cannot take DI); a second host pointing at a different database re-targets it and breaks decryption
    // everywhere. Derived WithWebHostBuilder factories are fine ONLY because they share this fixture's
    // container. Never create another Testcontainer fixture (Law 9).
    public const string AdminEmail = "admin@platform.test";

    /// <summary>Blind-index HMAC key for the test host — tests hash with it to locate users by e-mail in SQL.</summary>
    public const string BlindIndexKey = "integration-test-blind-index-key-32ch";

    /// <summary>The users.EmailHash value for an e-mail, exactly as the platform computes it.</summary>
    public static string EmailHashOf(string email)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(BlindIndexKey));
        return Convert.ToBase64String(
            hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())));
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"mp-storage-{Guid.CreateVersion7():N}");
    private readonly bool _soloMode;
    private WebApplicationFactory<Program> _factory = default!;

    public PlatformApiFactory()
        : this(soloMode: true)
    {
    }

    public static PlatformApiFactory PublisherOnly() => new(soloMode: false);

    private PlatformApiFactory(bool soloMode)
    {
        _soloMode = soloMode;
    }

    public HttpClient Client { get; private set; } = default!;
    public string ConnectionString => _postgres.GetConnectionString();
    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = CreateHost();
        Client = _factory.CreateClient();
    }

    /// <summary>
    /// A DERIVED host sharing this fixture's container/database — for scenarios needing different host
    /// config (low rate limits, Production environment, broken connection string). Dispose it in the test.
    /// Same-DB is what keeps the process-wide personal-data protector valid (see the invariant above).
    /// </summary>
    public WebApplicationFactory<Program> CreateHost(params (string Key, string Value)[] overrides) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Messaging:SoloMode", _soloMode ? "true" : "false");
            builder.UseSetting("ConnectionStrings:Write", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Read", _postgres.GetConnectionString());
            builder.UseSetting("RunMigrationsAtStartup", "true");
            // RLS is ON by default; pin the runtime-role password so ScalarAsUserAsync can authenticate as it.
            builder.UseSetting("Persistence:Rls:RuntimePassword", RlsRuntimePassword);
            // Configure the well-known admin email so the authz tests can bootstrap an admin via login.
            builder.UseSetting("Identity:Auth:AdminEmails:0", AdminEmail);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32b");
            // Blind-index key for encrypted-column lookups (users.EmailHash). Const so tests can compute
            // the same HMAC when they need to find a row by e-mail in raw SQL assertions.
            builder.UseSetting("Gdpr:Encryption:BlindIndexKey", BlindIndexKey);
            // A real 32-byte secrets master key so the tenant-secret protector validates even on a non-Development
            // derived host (the validator fail-fasts on a missing or dev-only key outside Development, like JWT/RLS).
            builder.UseSetting("Secrets:MasterKeys:1", "aW50ZWdyYXRpb24tdGVzdC1zZWNyZXRzLWtleS0wMzI=");
            builder.UseSetting("Jwt:Issuer", "test");
            builder.UseSetting("Jwt:Audience", "test");
            // Functional tests share one loopback IP partition; raise the rate limits so they aren't throttled.
            // (The brute-force/rate-limit scenarios assert 429 via a dedicated low-limit host, not this one.)
            builder.UseSetting("RateLimiting:GlobalPermitsPerMinute", "100000");
            builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "100000");
            // Files module: use the local-disk storage provider with an isolated per-run temp root.
            builder.UseSetting("Storage:Provider", "local");
            builder.UseSetting("Storage:Local:RootPath", _storageRoot);
            // Marketing: in-memory GA4/GSC + AI gateways — the pull → snapshot → analysis pipeline is assertable
            // offline (no Google credentials, no Claude API key).
            builder.UseSetting("Marketing:UseFakeGateways", "true");
            // Billing: in-memory Stripe gateway (the seam) — tests seed events/subscriptions through
            // FakeStripeGateway resolved from Services, so the FULL worker path is assertable offline.
            builder.UseSetting("Billing:Stripe:UseFakeGateway", "true");
            builder.UseSetting("Billing:Stripe:SuccessUrl", "https://app.test/billing/success");
            builder.UseSetting("Billing:Stripe:CancelUrl", "https://app.test/billing/cancel");
            // One config-driven subscription plan so the lifecycle + per-period grant paths are testable.
            builder.UseSetting("Billing:Subscriptions:Plans:0:PlanKey", "pro");
            builder.UseSetting("Billing:Subscriptions:Plans:0:StripePriceId", "price_test_pro");
            builder.UseSetting("Billing:Subscriptions:Plans:0:CreditsPerPeriod", "100");
            // Platform-plane gateway (tenant pays the SaaS) — the in-memory fake so the seam is assertable offline.
            builder.UseSetting("Platform:Payments:Provider", "fake");
            builder.UseSetting("Platform:Payments:Currency", "EUR");
            // Server-authoritative platform plan catalogue (the tenant picks a plan KEY, never a free-form amount).
            builder.UseSetting("Platform:Payments:Plans:pro:AmountMinorUnits", "4900");
            builder.UseSetting("Platform:Payments:Plans:pro:Currency", "EUR");
            builder.UseSetting("Platform:Payments:Plans:pro:Description", "Pro plan");

            foreach (var (key, value) in overrides)
            {
                builder.UseSetting(key, value);
            }
        });

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

    /// <summary>
    /// Runs a scalar query as the least-privilege RLS runtime role but with <c>app.is_system=on</c>, matching the
    /// Worker/Jobs system principal. This proves system work bypasses per-user policies without using the admin role.
    /// </summary>
    public async Task<T> ScalarAsSystemAsync<T>(string sql)
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
                "SELECT set_config('app.is_system', 'on', false), set_config('app.principal_id', '', false)";
            await setGucs.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Runs the same scalar query twice on one already-open runtime-role connection, restamping the session
    /// principal through the real <see cref="PrincipalSessionConnectionInterceptor"/> between executions.
    /// This pins the pooled-connection safety invariant: a connection reused across users must not keep stale GUCs.
    /// </summary>
    public async Task<(T First, T Second)> ScalarAsUsersOnSameRuntimeConnectionAsync<T>(
        Guid firstPrincipalUserId,
        Guid secondPrincipalUserId,
        string sql)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = RlsRuntimeRole,
            Password = RlsRuntimePassword,
        }.ConnectionString;

        var tenant = new MutableTenantContext
        {
            UserId = firstPrincipalUserId,
            IsSystem = false,
        };
        var interceptor = new PrincipalSessionConnectionInterceptor(tenant);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await interceptor.ConnectionOpenedAsync(conn, null!);
        var first = await ExecuteScalarAsync<T>(conn, sql);

        tenant.UserId = secondPrincipalUserId;
        await interceptor.ConnectionOpenedAsync(conn, null!);
        var second = await ExecuteScalarAsync<T>(conn, sql);

        return (first, second);
    }

    public static async Task<JsonElement> ReadData(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("data");
    }

    private static async Task<T> ExecuteScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? UserId { get; set; }
        public Guid? TenantId { get; set; }
        public bool IsSystem { get; set; }
        public string? IpAddress => null;
    }
}
