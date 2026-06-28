using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Messaging;
using ModularPlatform.Operations.Features.Demo;
using ModularPlatform.Operations.Features.DemoInvoke;
using ModularPlatform.Operations.Features.List;
using ModularPlatform.Operations.Features.Status;
using ModularPlatform.Operations.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Operations;

/// <summary>
/// Operations module: the reusable long-running-operation (202 + status) mechanism. Owns the <c>operations</c>
/// table, exposes <see cref="IOperationStore"/> for any module to create/complete operations, and ships a
/// canonical demo (POST /operations/demo → durable worker → GET /operations/{id}). Gated on
/// <c>Modules:Operations:Enabled</c>.
/// </summary>
public sealed class OperationsModule : IModule
{
    public string Name => "Operations";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(OperationsModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(OperationsModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<OperationsDbContext>(Name, write);
        services.AddModuleReadDbContext<OperationsDbContext>(read);

        services.AddScoped<IOperationStore, OperationStore>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapStartDemoOperation();
        endpoints.MapInvokeDemoCheck();
        endpoints.MapGetOperationStatus();
        endpoints.MapListMyOperations();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        options.Discovery.IncludeType<Messaging.RunDemoOperationHandler>();
        options.Discovery.IncludeType<Messaging.DemoQuickCheckHandler>();
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<OperationsDbContext>(services, adminConnectionString, Name, ct);
    }
}
