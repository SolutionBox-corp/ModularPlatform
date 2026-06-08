using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Audit;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module's WRITE DbContext with Wolverine's EF Core outbox integration, so a command
    /// handler can inject <see cref="Wolverine.EntityFrameworkCore.IDbContextOutbox{T}"/> and call
    /// <c>SaveChangesAndFlushMessagesAsync</c> — persisting business data AND outgoing integration events
    /// in ONE transaction (the outbox guarantee). The audit interceptor runs on save; xmin concurrency,
    /// tenant + soft-delete filters and the per-module audit table come from <see cref="PlatformDbContext"/>.
    /// Pair with <c>AddModuleReadDbContext</c> for the no-tracking read side.
    /// Context ctor must be <c>(DbContextOptions&lt;TContext&gt; options, ITenantContext tenant)</c>.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        string moduleName,
        string writeConnectionString)
        where TContext : PlatformDbContext
    {
        services.AddPlatformPersistence();

        var historyTable = $"__ef_migrations_{moduleName.ToLowerInvariant()}";

        services.AddDbContextWithWolverineIntegration<TContext>((sp, options) =>
            options
                .UseNpgsql(writeConnectionString, npg => npg.MigrationsHistoryTable(historyTable))
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        return services;
    }
}
