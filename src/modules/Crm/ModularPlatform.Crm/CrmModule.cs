using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Companies.CreateCompany;
using ModularPlatform.Crm.Features.Companies.DeleteCompany;
using ModularPlatform.Crm.Features.Companies.GetCompany;
using ModularPlatform.Crm.Features.Companies.ListCompanies;
using ModularPlatform.Crm.Features.Companies.UpdateCompany;
using ModularPlatform.Crm.Features.Contacts.AddInteraction;
using ModularPlatform.Crm.Features.Contacts.CreateContact;
using ModularPlatform.Crm.Features.Contacts.DeleteContact;
using ModularPlatform.Crm.Features.Contacts.GetContact;
using ModularPlatform.Crm.Features.Contacts.ListContacts;
using ModularPlatform.Crm.Features.Contacts.ListInteractions;
using ModularPlatform.Crm.Features.Contacts.UpdateContact;
using ModularPlatform.Crm.Features.Deals.CreateDeal;
using ModularPlatform.Crm.Features.Deals.DeleteDeal;
using ModularPlatform.Crm.Features.Deals.GetDeal;
using ModularPlatform.Crm.Features.Deals.ListDeals;
using ModularPlatform.Crm.Features.Deals.MoveDealStage;
using ModularPlatform.Crm.Features.Deals.UpdateDeal;
using ModularPlatform.Crm.Features.Kanban.CreateBoard;
using ModularPlatform.Crm.Features.Kanban.CreateCard;
using ModularPlatform.Crm.Features.Kanban.CreateColumn;
using ModularPlatform.Crm.Features.Kanban.DeleteBoard;
using ModularPlatform.Crm.Features.Kanban.DeleteCard;
using ModularPlatform.Crm.Features.Kanban.GetBoard;
using ModularPlatform.Crm.Features.Kanban.ListBoards;
using ModularPlatform.Crm.Features.Kanban.MoveCard;
using ModularPlatform.Crm.Features.Meetings.CancelMeeting;
using ModularPlatform.Crm.Features.Meetings.CompleteMeeting;
using ModularPlatform.Crm.Features.Meetings.CreateMeeting;
using ModularPlatform.Crm.Features.Meetings.GetMeeting;
using ModularPlatform.Crm.Features.Meetings.ListMeetings;
using ModularPlatform.Crm.Features.Meetings.UpdateMeeting;
using ModularPlatform.Crm.Features.Tasks.CompleteTask;
using ModularPlatform.Crm.Features.Tasks.CreateTask;
using ModularPlatform.Crm.Features.Tasks.DeleteTask;
using ModularPlatform.Crm.Features.Tasks.GetTask;
using ModularPlatform.Crm.Features.Tasks.ListTasks;
using ModularPlatform.Crm.Features.Tasks.UpdateTask;
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

        endpoints.MapCreateMeeting();
        endpoints.MapGetMeeting();
        endpoints.MapListMeetings();
        endpoints.MapUpdateMeeting();
        endpoints.MapCancelMeeting();
        endpoints.MapCompleteMeeting();

        endpoints.MapCreateDeal();
        endpoints.MapGetDeal();
        endpoints.MapListDeals();
        endpoints.MapUpdateDeal();
        endpoints.MapMoveDealStage();
        endpoints.MapDeleteDeal();

        endpoints.MapCreateTask();
        endpoints.MapGetTask();
        endpoints.MapListTasks();
        endpoints.MapUpdateTask();
        endpoints.MapCompleteTask();
        endpoints.MapDeleteTask();

        endpoints.MapCreateCompany();
        endpoints.MapGetCompany();
        endpoints.MapListCompanies();
        endpoints.MapUpdateCompany();
        endpoints.MapDeleteCompany();

        endpoints.MapCreateBoard();
        endpoints.MapListBoards();
        endpoints.MapGetBoard();
        endpoints.MapDeleteBoard();
        endpoints.MapCreateColumn();
        endpoints.MapCreateCard();
        endpoints.MapMoveCard();
        endpoints.MapDeleteCard();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Consume Identity's UserRegistered to seed a starter workspace (first cross-module event for CRM).
        // Registered EXPLICITLY (cross-assembly discovery is unreliable for module assemblies); public shell.
        options.Discovery.IncludeType<Messaging.ProvisionCrmWorkspaceHandler>();
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
