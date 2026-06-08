using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Identity.Persistence;

/// <summary>
/// Design-time only: lets <c>dotnet ef migrations add</c> construct the context (its runtime ctor needs
/// <see cref="ITenantContext"/>). Uses a system tenant + a throwaway connection string — never used at runtime.
/// </summary>
internal sealed class IdentityDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=modularplatform_design;Username=postgres;Password=postgres",
                npg => npg.MigrationsHistoryTable("__ef_migrations_identity"))
            .Options;

        return new IdentityDbContext(options, new SystemTenantContext());
    }
}
