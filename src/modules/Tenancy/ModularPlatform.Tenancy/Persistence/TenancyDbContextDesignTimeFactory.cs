using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Tenancy.Persistence;

/// <summary>
/// Design-time only: lets <c>dotnet ef migrations add</c> construct the context (its runtime ctor needs
/// <see cref="ITenantContext"/>). Uses a system tenant + a throwaway connection string — never used at runtime.
/// </summary>
internal sealed class TenancyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql("Host=localhost;Database=modularplatform_design;Username=postgres;Password=postgres",
                npg => npg.MigrationsHistoryTable("__ef_migrations_tenancy"))
            .Options;

        return new TenancyDbContext(options, new SystemTenantContext());
    }
}
