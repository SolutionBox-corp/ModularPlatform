using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Analyses.GetAnalysis;
using ModularPlatform.Marketing.Features.Analyses.ListAnalyses;
using ModularPlatform.Marketing.Features.Pulls.GetPullStatus;
using ModularPlatform.Marketing.Features.Pulls.TriggerPull;
using ModularPlatform.Marketing.Features.Snapshots.ListSnapshots;
using ModularPlatform.Marketing.Integrations;
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

        services.AddScoped<IGa4Gateway, FakeGa4Gateway>();
        services.AddScoped<IGscGateway, FakeGscGateway>();

        // Analysis brain: the deterministic fake when Marketing:UseFakeGateways=true (dev/tests, no API key needed),
        // otherwise the real Claude-backed gateway bound to Marketing:Claude.
        if (configuration.GetValue("Marketing:UseFakeGateways", false))
        {
            services.AddScoped<IMarketingAiGateway, FakeMarketingAiGateway>();
        }
        else
        {
            services.Configure<MarketingClaudeOptions>(configuration.GetSection(MarketingClaudeOptions.SectionName));
            services.AddScoped<IMarketingAiGateway, ClaudeMarketingGateway>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTriggerPull();
        endpoints.MapGetPullStatus();
        endpoints.MapListSnapshots();
        endpoints.MapListAnalyses();
        endpoints.MapGetAnalysis();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        options.Discovery.IncludeType<Messaging.RunDataPullHandler>();
        options.Discovery.IncludeType<Messaging.MarketingDataPulledHandler>();
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<MarketingDbContext>(services, adminConnectionString, Name, ct);
    }
}
