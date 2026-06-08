using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace ModularPlatform.Abstractions;

/// <summary>
/// A pluggable platform module. The host scans loaded assemblies for implementations, gates each
/// on <c>Modules:{Name}:Enabled</c>, and wires it. The same base ships as different products by
/// toggling the module set. A module owns its feature folders, its DbContext registration, its
/// endpoints, and its messaging routes/handlers — but never references another module's Core.
/// </summary>
public interface IModule
{
    /// <summary>Stable module name; also the config key under <c>Modules:{Name}</c>.</summary>
    string Name { get; }

    /// <summary>Register the module's services, DbContext, validators and handlers.</summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Map the module's HTTP endpoints (Minimal API extension methods).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Configure the module's Wolverine message handlers, routes and listeners.</summary>
    void ConfigureMessaging(WolverineOptions options);

    /// <summary>
    /// Applies the module's own EF Core migrations (its Core owns its internal DbContext). Called by the
    /// MigrationService host before the Api serves. Default no-op for modules without a database.
    /// </summary>
    Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}
