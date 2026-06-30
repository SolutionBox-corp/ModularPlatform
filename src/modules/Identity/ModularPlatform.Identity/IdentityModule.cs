using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Gdpr;
using ModularPlatform.Identity.Jobs;
using Quartz;
using ModularPlatform.Identity.Features.Admin.AssignRole;
using ModularPlatform.Identity.Features.Admin.GetUserDetail;
using ModularPlatform.Identity.Features.Admin.IssueMachineToken;
using ModularPlatform.Identity.Features.Admin.RevokeRole;
using ModularPlatform.Identity.Features.Audit.GetUserAuditTrail;
using ModularPlatform.Identity.Features.PlatformAdmin.GetPlatformUserAudit;
using ModularPlatform.Identity.Features.PlatformAdmin.ListPlatformUsers;
using ModularPlatform.Identity.Features.Auth.ForgotPassword;
using ModularPlatform.Identity.Features.Auth.Login;
using ModularPlatform.Identity.Features.Auth.Logout;
using ModularPlatform.Identity.Features.Auth.RefreshToken;
using ModularPlatform.Identity.Features.Auth.ResetPassword;
using ModularPlatform.Identity.Features.Auth.VerifyEmail;
using ModularPlatform.Identity.Features.Users.GetProfile;
using ModularPlatform.Identity.Features.Users.ListTenantUsers;
using ModularPlatform.Identity.Features.Users.RequestEmailVerification;
using ModularPlatform.Identity.Features.Users.RegisterUser;
using ModularPlatform.Identity.Features.Users.UpdateProfile;
using ModularPlatform.Identity.Features.Users.ChangePassword;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Identity;

/// <summary>
/// CANONICAL module wiring. The host discovers this via <see cref="IModule"/>, gated on
/// <c>Modules:Identity:Enabled</c>. It owns its DbContext, handlers, validators, endpoints — and nothing
/// outside this assembly references its Core types (only <c>ModularPlatform.Identity.Contracts</c>).
/// </summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var (write, read) = ModuleConnectionStrings.GetWriteAndRead(configuration);

        services.AddCqrs(typeof(IdentityModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(IdentityModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<IdentityDbContext>(Name, write);
        services.AddModuleReadDbContext<IdentityDbContext>(read);

        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddOptions<PasswordResetOptions>().BindConfiguration(PasswordResetOptions.SectionName);
        services.AddOptions<EmailVerificationOptions>().BindConfiguration(EmailVerificationOptions.SectionName);

        // Authorization seeding: permissions catalog + system admin role + admin assignment (config-driven).
        services.AddOptions<IdentityAuthOptions>().BindConfiguration(IdentityAuthOptions.SectionName);
        services.AddHostedService<IdentitySeeder>();

        // Seals pre-encryption user rows (EmailHash + [Encrypted] columns) — no-op on fresh databases.
        services.AddHostedService<PiiEncryptionBackfill>();

        // GDPR: Identity owns the account PII (email/name) — export it and erase it on request.
        services.AddScoped<IExportPersonalData, IdentityPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, IdentityPersonalDataEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRegisterUser();
        endpoints.MapListTenantUsers();
        endpoints.MapGetProfile();
        endpoints.MapUpdateProfile();
        endpoints.MapChangePassword();
        endpoints.MapLogin();
        endpoints.MapForgotPassword();
        endpoints.MapResetPassword();
        endpoints.MapVerifyEmail();
        endpoints.MapRefreshToken();
        endpoints.MapLogout();
        endpoints.MapRequestEmailVerification();
        endpoints.MapAssignRole();
        endpoints.MapRevokeRole();
        endpoints.MapGetUserDetail();
        endpoints.MapIssueMachineToken();
        endpoints.MapGetUserAuditTrail();
        endpoints.MapListPlatformUsers();
        endpoints.MapGetPlatformUserAudit();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Identity currently publishes UserRegisteredIntegrationEvent (via the outbox) but consumes nothing.
        // Wolverine auto-discovers any message handlers added later in this assembly.
    }

    public void RegisterJobs(IServiceCollectionQuartzConfigurator quartz, IConfiguration configuration)
    {
        // Daily sweep that deletes long-expired refresh tokens (bounds the rotation table). Cron in UTC (Law #7).
        var cron = configuration["Modules:Identity:Jobs:PurgeRefreshTokensCron"] ?? "0 0 3 * * ?"; // 03:00 UTC daily
        var key = new JobKey("identity-purge-refresh-tokens");
        quartz.AddJob<IdentityPurgeRefreshTokensJob>(key);
        quartz.AddTrigger(trigger => trigger.ForJob(key)
            .WithCronSchedule(cron, x => x.InTimeZone(TimeZoneInfo.Utc)));
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        // Migrations run on the ADMIN connection — the DI-registered context uses the RLS runtime role,
        // which cannot run DDL. The RLS bootstrapper (host, post-migration) then provisions role + policies.
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<IdentityDbContext>(services, adminConnectionString, Name, ct);
    }
}
