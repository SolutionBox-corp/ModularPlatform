using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Marketing;

/// <summary>
/// Marketing module: pulls free marketing-data sources (GA4, Search Console, PostHog, Reddit, Google Trends),
/// persists raw pulls + normalized metric snapshots, runs AI (Claude) analyses, and powers the "vibe marketing"
/// agentic chat. Owns the <c>marketing</c> schema. Gated on <c>Modules:Marketing:Enabled</c>.
/// </summary>
public sealed class MarketingModule : IModule
{
    public string Name => "Marketing";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(MarketingModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(MarketingModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<MarketingDbContext>(Name, write);
        services.AddModuleReadDbContext<MarketingDbContext>(read);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Endpoints are added in Phase 2 (pulls) and Phase 5 (vibe chat).
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Wolverine handlers are added in Phase 2/3 (pull + analysis workers).
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<MarketingDbContext>(services, adminConnectionString, Name, ct);
    }
}
