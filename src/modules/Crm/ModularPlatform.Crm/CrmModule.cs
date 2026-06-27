using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts.AddInteraction;
using ModularPlatform.Crm.Features.Contacts.CreateContact;
using ModularPlatform.Crm.Features.Contacts.DeleteContact;
using ModularPlatform.Crm.Features.Contacts.GetContact;
using ModularPlatform.Crm.Features.Contacts.ListContacts;
using ModularPlatform.Crm.Features.Contacts.ListInteractions;
using ModularPlatform.Crm.Features.Contacts.UpdateContact;
using ModularPlatform.Crm.Gdpr;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Crm;

/// <summary>
/// CRM module wiring (mirrors the canonical <c>IdentityModule</c>). The host discovers this via
/// <see cref="IModule"/>, gated on <c>Modules:Crm:Enabled</c>; per-tenant visibility is the live
/// <c>RequireModule("crm")</c> entitlement. Owns its <see cref="CrmDbContext"/>; nothing outside this assembly
/// references its Core types (only <c>ModularPlatform.Crm.Contracts</c>).
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

        // GDPR: CRM holds PII (contacts) — export it and crypto-shred-scrub it on request.
        services.AddScoped<IExportPersonalData, CrmPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, CrmPersonalDataEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCreateContact();
        endpoints.MapGetContact();
        endpoints.MapListContacts();
        endpoints.MapUpdateContact();
        endpoints.MapDeleteContact();
        endpoints.MapAddInteraction();
        endpoints.MapListInteractions();
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
