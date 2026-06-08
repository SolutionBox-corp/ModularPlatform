using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Gdpr;
using ModularPlatform.Identity.Features.Auth.Login;
using ModularPlatform.Identity.Features.Auth.RefreshToken;
using ModularPlatform.Identity.Features.Users.GetProfile;
using ModularPlatform.Identity.Features.Users.RegisterUser;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
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
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(IdentityModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(IdentityModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<IdentityDbContext>(Name, write);
        services.AddModuleReadDbContext<IdentityDbContext>(read);

        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();

        // GDPR: Identity owns the account PII (email/name) — export it and erase it on request.
        services.AddScoped<IExportPersonalData, IdentityPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, IdentityPersonalDataEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRegisterUser();
        endpoints.MapGetProfile();
        endpoints.MapLogin();
        endpoints.MapRefreshToken();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Identity currently publishes UserRegisteredIntegrationEvent (via the outbox) but consumes nothing.
        // Wolverine auto-discovers any message handlers added later in this assembly.
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
