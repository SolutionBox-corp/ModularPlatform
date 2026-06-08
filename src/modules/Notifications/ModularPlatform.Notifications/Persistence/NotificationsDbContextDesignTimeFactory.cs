using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Notifications.Persistence;

/// <summary>
/// Design-time only: lets <c>dotnet ef migrations add</c> construct the context (its runtime ctor needs
/// <see cref="ITenantContext"/>). Uses a system tenant + a throwaway connection string — never used at runtime.
/// </summary>
internal sealed class NotificationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost;Database=modularplatform_design;Username=postgres;Password=postgres",
                npg => npg.MigrationsHistoryTable("__ef_migrations_notifications"))
            .Options;

        return new NotificationsDbContext(options, new SystemTenantContext());
    }
}
