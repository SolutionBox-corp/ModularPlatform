using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using ModularPlatform.Web.Errors;

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

        services.AddLocalization(o => o.ResourcesPath = "Localization");

        // Outer pipeline behaviors. ORDER MATTERS — registration order == execution order.
        // (Telemetry is registered first by AddPlatformTelemetry in the host.)
        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        AddJwt(services, configuration);
        AddRateLimiter(services);

        return services;
    }

    /// <summary>
    /// The request pipeline, in order: security headers -> request localization -> global exception
    /// (RFC 9457) -> rate limiter -> auth -> authorization. Call before mapping module endpoints.
    /// </summary>
    public static WebApplication UsePlatformWeb(this WebApplication app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();

        app.UseRequestLocalization(o =>
        {
            o.SetDefaultCulture("en")
                .AddSupportedCultures(SupportedCultures.Split(','))
                .AddSupportedUICultures(SupportedCultures.Split(','));
        });

        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    private static void AddJwt(IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(string.IsNullOrEmpty(jwt.SigningKey)
                            ? new string('0', 32) // placeholder so DI builds in dev; real key from env/KeyVault
                            : jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
    }

    private static void AddRateLimiter(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partition by authenticated user, else by remote IP. Redis-backed distributed limiter
            // is swapped in for multi-instance; the partition key is the stable seam.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var key = context.User.Identity?.IsAuthenticated == true
                    ? context.User.Identity!.Name ?? "user"
                    : context.Connection.RemoteIpAddress?.ToString() ?? "anon";

                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100,
                    TokensPerPeriod = 100,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true,
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
