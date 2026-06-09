using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.Download;
using ModularPlatform.Files.Features.List;
using ModularPlatform.Files.Features.Upload;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using ModularPlatform.Storage;
using Wolverine;

namespace ModularPlatform.Files;

/// <summary>
/// Files module: owns file METADATA (the <c>file_objects</c> table, RLS-isolated per user) and the upload/download/
/// list slices. The bytes live behind the <see cref="IFileStorage"/> building block (local disk for dev, S3-compatible
/// for prod), selected by <c>Storage:Provider</c>. Gated on <c>Modules:Files:Enabled</c>.
/// </summary>
public sealed class FilesModule : IModule
{
    public string Name => "Files";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(FilesModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(FilesModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<FilesDbContext>(Name, write);
        services.AddModuleReadDbContext<FilesDbContext>(read);

        // Blob storage (local | s3), selected by Storage:Provider. Idempotent if another module also registers it.
        services.AddPlatformStorage(configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapUploadFile();
        endpoints.MapDownloadFile();
        endpoints.MapListFiles();
    }

    // No cross-module integration events — the module owns only its own metadata + storage.
    public void ConfigureMessaging(WolverineOptions options) { }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<FilesDbContext>(services, adminConnectionString, Name, ct);
    }
}
