using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Crm;

/// <summary>
/// CRM module wiring (mirrors the canonical <c>IdentityModule</c>). The host discovers this via
/// <see cref="IModule"/>, gated on <c>Modules:Crm:Enabled</c>. It owns its <see cref="CrmDbContext"/>; nothing
/// outside this assembly references its Core types (only <c>ModularPlatform.Crm.Contracts</c>).
///
/// Phase 0 scaffolding: the module is registered in all four hosts and the migration/RLS pipeline, but has no
/// features yet. Endpoints, integration-event handlers, jobs and the GDPR export/erase ports are added as the
/// Contacts / Meetings / Outreach / Kanban features land (the module holds PII, so it WILL register
/// <c>IExportPersonalData</c> + <c>IErasePersonalData</c> once Contacts exist).
/// </summary>
public sealed class CrmModule : IModule
{
    public string Name => "Crm";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(CrmModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(CrmModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<CrmDbContext>(Name, write);
        services.AddModuleReadDbContext<CrmDbContext>(read);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // No endpoints yet. Each feature adds its endpoint extension here, mapping RELATIVE routes ("/crm/...").
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // No integration-event handlers yet. Each one is registered explicitly via
        // options.Discovery.IncludeType<TheHandler>() when added.
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        // Migrations run on the ADMIN connection — the DI-registered context uses the RLS runtime role, which
        // cannot run DDL. The RLS bootstrapper (host, post-migration) then provisions role + policies.
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<CrmDbContext>(services, adminConnectionString, Name, ct);
    }
}
