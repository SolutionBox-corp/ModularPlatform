using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Marketing.Persistence;

/// <summary>Design-time only: lets <c>dotnet ef migrations add</c> construct the context. Never used at runtime.</summary>
internal sealed class MarketingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MarketingDbContext>
{
    public MarketingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MarketingDbContext>()
            .UseNpgsql("Host=localhost;Database=modularplatform_design;Username=postgres;Password=postgres",
                npg => npg.MigrationsHistoryTable("__ef_migrations_marketing"))
            .Options;

        return new MarketingDbContext(options, new SystemTenantContext());
    }
}
