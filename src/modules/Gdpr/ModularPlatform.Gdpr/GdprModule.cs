using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Features.Consents.GetConsents;
using ModularPlatform.Gdpr.Features.Consents.GrantConsent;
using ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;
using ModularPlatform.Gdpr.Features.Erasure.RequestErasure;
using ModularPlatform.Gdpr.Features.Export.ExportUserData;
using ModularPlatform.Gdpr.Persistence;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using Wolverine;

namespace ModularPlatform.Gdpr;

/// <summary>
/// GDPR orchestrator module. Boundary-clean: it depends ONLY on the Abstractions ports
/// (<see cref="IExportPersonalData"/>, <see cref="IErasePersonalData"/>) and never on another module's Core.
/// It fans portability/erasure across whichever modules implement those ports, owns the consent log and the
/// per-subject crypto-shredding key envelope, and publishes <c>UserErasureRequested</c> via the outbox.
/// </summary>
public sealed class GdprModule : IModule
{
    public string Name => "Gdpr";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(GdprModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(GdprModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<GdprDbContext>(Name, write);
        services.AddModuleReadDbContext<GdprDbContext>(read);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapExportUserData();
        endpoints.MapRequestErasure();
        endpoints.MapGrantConsent();
        endpoints.MapWithdrawConsent();
        endpoints.MapGetConsents();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Register this module's message handlers explicitly (cross-assembly conventional discovery is unreliable).
        options.Discovery.IncludeType<Messaging.UserErasureRequestedHandler>();
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GdprDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
