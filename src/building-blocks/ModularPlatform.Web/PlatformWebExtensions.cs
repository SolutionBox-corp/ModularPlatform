using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using ModularPlatform.Web.Errors;
using ModularPlatform.Web.RateLimiting;
using StackExchange.Redis;

namespace ModularPlatform.Web;

public static class PlatformWebExtensions
{
    private const string SupportedCultures = "en,cs";

    /// <summary>
    /// Wires the platform's web cross-cutting concerns once in the API host: localization (resx, en/cs),
    /// tenant context + clock, JWT bearer auth, partitioned rate limiting, and the two outer-most CQRS
    /// behaviors (telemetry is added by the Telemetry building block; logging + validation here).
    /// </summary>
    public static IServiceCollection AddPlatformWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ITenantContext, HttpTenantContext>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IHostEnvironmentAccessor, HostEnvironmentAccessor>();

        // NO ResourcesPath: SharedResource's namespace already mirrors the Localization/ folder, so the
        // manifest name is ModularPlatform.Web.Localization.SharedResource. A ResourcesPath would make the
        // factory probe ...Localization.Localization.SharedResource — never found, every `detail` silently
        // fell back to the exception message and Accept-Language did nothing (caught by PL-3).
        services.AddLocalization();

        // Outer pipeline behaviors. ORDER MATTERS — registration order == execution order.
        // (Telemetry is registered first by AddPlatformTelemetry in the host.)
        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        AddForwardedHeaders(services, configuration);
        AddJwt(services, configuration);
        AddRateLimiter(services, configuration);

        return services;
    }

    private static void AddForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        // The forwarded client IP feeds audit + the auth rate-limiter, so a missing trust list behind a proxy is a
        // spoofing hole — the validator fail-fasts on it in Production. The framework ForwardedHeadersOptions is
        // built from the same settings (KnownProxies/KnownNetworks parsed once) and consumed by UseForwardedHeaders.
        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
            ?? new ForwardedHeadersSettings();

        services.AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(ForwardedHeadersSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = settings.ForwardLimit;

            if (!settings.HasTrustList)
            {
                // No explicit trust list: keep ASP.NET's loopback defaults (safe for local/dev; Production is
                // blocked from reaching here with Enabled=true by the validator).
                return;
            }

            // Replace the loopback defaults with the configured trust list.
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var proxy in settings.KnownProxies)
            {
                o.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var cidr in settings.KnownNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
            }
        });
    }

    /// <summary>
    /// The request pipeline, in order: security headers -> request localization -> global exception
    /// (RFC 9457) -> authentication -> rate limiter -> authorization. Call before mapping module endpoints.
    /// The rate limiter runs AFTER authentication so the global limiter can partition by the authenticated
    /// user (its claims must already be populated); the per-IP "auth" policy on anonymous endpoints is unaffected.
    /// </summary>
    public static WebApplication UsePlatformWeb(this WebApplication app)
    {
        // Resolve the real client IP behind a proxy for rate limiting + audit. Options (incl. the KnownProxies/
        // KnownNetworks trust list) are bound + validated in AddForwardedHeaders; the validator fail-fasts a
        // Production host that left the trust list empty (which would trust spoofed X-Forwarded-For from anyone).
        // Enabled=false (a host with no reverse proxy) genuinely skips the middleware — it is the validator's
        // advertised escape hatch, so it must actually turn the middleware off.
        if (app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value.Enabled)
        {
            app.UseForwardedHeaders();
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();

        app.UseRequestLocalization(o =>
        {
            o.SetDefaultCulture("en")
                .AddSupportedCultures(SupportedCultures.Split(','))
                .AddSupportedUICultures(SupportedCultures.Split(','));
        });

        app.UseMiddleware<GlobalExceptionMiddleware>();
        // Authentication BEFORE the rate limiter: the global limiter partitions by the authenticated user's claim,
        // which only exists after auth has run. (A pre-auth limiter saw no claims and collapsed every authenticated
        // caller into the shared IP bucket — caught by PL11.)
        app.UseAuthentication();
        // Resolve the subdomain→tenant AFTER auth (token tenant_id exists) and BEFORE the rate limiter, so a host /
        // token mismatch is rejected early. No-op for apex/localhost and when the Tenancy module is disabled.
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        return app;
    }

    private static void AddJwt(IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // An empty signing key is only reachable in Development (JwtOptionsValidator fail-fasts outside it). Fall back
        // to a RANDOM per-process key — NEVER the well-known all-zeros placeholder, which a Staging box mistakenly run
        // as Development could leak as a trivially-forgeable admin key. TokenIssuer reads IOptions<JwtOptions>, so push
        // the same key there (PostConfigure) so issuance and validation agree.
        var signingKey = string.IsNullOrEmpty(jwt.SigningKey)
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            : jwt.SigningKey;
        if (string.IsNullOrEmpty(jwt.SigningKey))
        {
            services.PostConfigure<JwtOptions>(o => o.SigningKey = signingKey);
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Role claims are emitted as type "role" — make RequireRole / IsInRole match them.
                    RoleClaimType = AuthorizationClaims.Role,
                };
            });

        services.AddAuthorization();
    }

    private static void AddRateLimiter(IServiceCollection services, IConfiguration configuration)
    {
        // Limits are config-driven (defaults match the production posture). A deployment can tune them, and the
        // integration-test host raises them so functional tests aren't throttled by the shared loopback IP
        // partition — a dedicated low-limit host is what the brute-force/rate-limit tests use to assert 429.
        var globalPermits = configuration.GetValue<int?>("RateLimiting:GlobalPermitsPerMinute") ?? 100;
        var authPermits = configuration.GetValue<int?>("RateLimiting:AuthPermitsPerMinute") ?? 10;
        var redisConnectionString = configuration.GetValue<string>("Redis:ConnectionString");
        var useRedis = configuration.GetValue<bool?>("RateLimiting:Redis:Enabled")
            ?? !string.IsNullOrWhiteSpace(redisConnectionString);
        IConnectionMultiplexer? redis = null;
        if (useRedis)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException(
                    "RateLimiting:Redis:Enabled requires Redis:ConnectionString.");
            }

            var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
            redisOptions.AbortOnConnectFail = false;
            redis = ConnectionMultiplexer.Connect(redisOptions);
            services.TryAddSingleton(redis);
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Emit Retry-After on a 429 when the limiter knows when the next permit frees up (token-bucket
            // replenishment / fixed-window reset) — clients back off intelligently instead of hammering.
            options.OnRejected = async (rejected, ct) =>
            {
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    rejected.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                // Keep the 429 on the same RFC 9457 contract as every other error — an empty body reads like a network
                // failure and invites aggressive retries (which is exactly what the limiter is defending against).
                if (!rejected.HttpContext.Response.HasStarted)
                {
                    rejected.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    rejected.HttpContext.Response.ContentType = "application/problem+json";
                    await rejected.HttpContext.Response.WriteAsJsonAsync(
                        new
                        {
                            type = "https://errors.modularplatform.dev/error.rate_limited",
                            title = "error.rate_limited",
                            status = StatusCodes.Status429TooManyRequests,
                            detail = "Too many requests.",
                            errorCode = "error.rate_limited",
                        },
                        ct);
                }
            };

            // Partition by authenticated user (the token's subject id), else by remote IP. The user id comes from the
            // NameIdentifier claim (TokenIssuer emits it) — NOT Identity.Name, which is null because no name claim is
            // issued, and which would collapse EVERY authenticated caller into one shared bucket. Redis-backed
            // distributed limiter is swapped in for multi-instance; the partition key is the stable seam.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var key = context.User.Identity?.IsAuthenticated == true
                    ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "user"
                    : context.Connection.RemoteIpAddress?.ToString() ?? "anon";

                if (redis is not null)
                {
                    return RateLimitPartition.Get(
                        key,
                        partitionKey => new RedisFixedWindowRateLimiter(
                            redis.GetDatabase(),
                            $"rl:global:{partitionKey}",
                            globalPermits,
                            TimeSpan.FromMinutes(1)));
                }

                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = globalPermits,
                    TokensPerPeriod = globalPermits,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true,
                    QueueLimit = 0,
                });
            });

            // Tight per-IP limit for credential endpoints (/login, /refresh) — brute-force defence in depth on
            // top of per-account lockout. Endpoints opt in with .RequireRateLimiting("auth").
            options.AddPolicy("auth", context =>
            {
                var key = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                if (redis is not null)
                {
                    return RateLimitPartition.Get(
                        key,
                        partitionKey => new RedisFixedWindowRateLimiter(
                            redis.GetDatabase(),
                            $"rl:auth:{partitionKey}",
                            authPermits,
                            TimeSpan.FromMinutes(1)));
                }

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authPermits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });
    }
}

internal sealed class HostEnvironmentAccessor(IHostEnvironment env) : IHostEnvironmentAccessor
{
    public bool IsDevelopment => env.IsDevelopment();
}
