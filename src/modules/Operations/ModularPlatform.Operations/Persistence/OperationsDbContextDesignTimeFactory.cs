using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Operations.Persistence;

/// <summary>Design-time only: lets <c>dotnet ef migrations add</c> construct the context. Never used at runtime.</summary>
internal sealed class OperationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OperationsDbContext>
{
    public OperationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql("Host=localhost;Database=modularplatform_design;Username=postgres;Password=postgres",
                npg => npg.MigrationsHistoryTable("__ef_migrations_operations"))
            .Options;

        return new OperationsDbContext(options, new SystemTenantContext());
    }
}
