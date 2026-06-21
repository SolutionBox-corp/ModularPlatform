using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Analyses.GetAnalysis;
using ModularPlatform.Marketing.Features.Analyses.ListAnalyses;
using ModularPlatform.Marketing.Features.Pulls.GetPullStatus;
using ModularPlatform.Marketing.Features.Pulls.ListPulls;
using ModularPlatform.Marketing.Features.Pulls.TriggerPull;
using ModularPlatform.Marketing.Features.Snapshots.ListSnapshots;
using ModularPlatform.Marketing.Features.Vibe.DeleteConversation;
using ModularPlatform.Marketing.Features.Vibe.GetConversation;
using ModularPlatform.Marketing.Features.Vibe.ListConversations;
using ModularPlatform.Marketing.Features.Vibe.SendMessage;
using ModularPlatform.Marketing.Features.Vibe.StartConversation;
using ModularPlatform.Marketing.Features.Vibe.StreamMessage;
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

        // Data-source gateways (GA4 + Search Console) + analysis/vibe brains: the deterministic fakes when
        // Marketing:UseFakeGateways=true (dev/tests, no credentials needed), otherwise the real SDK-backed gateways
        // — the Google ones bound to Marketing:Google, the Claude ones to Marketing:Claude. A missing key/target
        // surfaces at call time rather than at boot, so the fakes stay the only path the test harness exercises.
        if (configuration.GetValue("Marketing:UseFakeGateways", false))
        {
            services.AddScoped<IGa4Gateway, FakeGa4Gateway>();
            services.AddScoped<IGscGateway, FakeGscGateway>();
            services.AddScoped<IMarketingAiGateway, FakeMarketingAiGateway>();
            services.AddScoped<IVibeAgentGateway, FakeVibeAgentGateway>();
        }
        else
        {
            services.Configure<MarketingGoogleOptions>(configuration.GetSection(MarketingGoogleOptions.SectionName));
            services.AddScoped<IGa4Gateway, RealGa4Gateway>();
            services.AddScoped<IGscGateway, RealGscGateway>();

            services.Configure<MarketingClaudeOptions>(configuration.GetSection(MarketingClaudeOptions.SectionName));
            services.AddScoped<IMarketingAiGateway, ClaudeMarketingGateway>();
            services.AddScoped<IVibeAgentGateway, ClaudeVibeAgentGateway>();
        }

        // GDPR data-portability + erasure ports (fanned out by the Gdpr module). Both MUST be registered or the
        // module's personal data is silently skipped from export/erasure.
        services.AddScoped<IExportPersonalData, ModularPlatform.Marketing.Gdpr.MarketingPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, ModularPlatform.Marketing.Gdpr.MarketingPersonalDataEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTriggerPull();
        endpoints.MapGetPullStatus();
        endpoints.MapListPulls();
        endpoints.MapListSnapshots();
        endpoints.MapListAnalyses();
        endpoints.MapGetAnalysis();

        endpoints.MapStartConversation();
        endpoints.MapSendMessage();
        endpoints.MapStreamMessage();
        endpoints.MapListConversations();
        endpoints.MapGetConversation();
        endpoints.MapDeleteConversation();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        options.Discovery.IncludeType<Messaging.RunDataPullHandler>();
        options.Discovery.IncludeType<Messaging.MarketingDataPulledHandler>();
        options.Discovery.IncludeType<Messaging.RunVibeAgentTurnHandler>();
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<MarketingDbContext>(services, adminConnectionString, Name, ct);
    }
}
