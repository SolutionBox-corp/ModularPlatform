using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Audit;
using ModularPlatform.Persistence.Rls;
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

        // The bootstrapper inspects this context's model for IUserOwned tables to protect with RLS.
        services.AddSingleton(new RlsManagedContext(typeof(TContext)));

        var historyTable = $"__ef_migrations_{moduleName.ToLowerInvariant()}";

        services.AddDbContextWithWolverineIntegration<TContext>((sp, options) =>
        {
            // Runtime data connection uses the least-privilege role (subject to RLS) when enabled; migrations
            // run separately on the admin connection via PlatformMigrator. Admin connection when RLS is off.
            var rls = sp.GetRequiredService<IOptions<RlsOptions>>().Value;
            var runtimeConnectionString = RlsConnectionString.ForRuntime(writeConnectionString, rls);

            options
                .UseNpgsql(runtimeConnectionString, npg => npg.MigrationsHistoryTable(historyTable))
                .AddInterceptors(
                    sp.GetRequiredService<TenantStampingInterceptor>(),
                    sp.GetRequiredService<AuditInterceptor>());

            if (rls.Enabled)
            {
                options.AddInterceptors(sp.GetRequiredService<PrincipalSessionConnectionInterceptor>());
            }
        });

        return services;
    }
}
