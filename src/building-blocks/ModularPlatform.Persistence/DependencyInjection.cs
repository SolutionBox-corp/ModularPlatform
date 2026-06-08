using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence.Audit;
using ModularPlatform.Persistence.Behaviors;

namespace ModularPlatform.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform persistence pipeline once per host: the singleton
    /// <see cref="AuditInterceptor"/> and the command-only <see cref="ConcurrencyRetryBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <summary>
    /// Registers the minimal platform services a NON-web host (Worker/Jobs/Migration) needs before
    /// wiring modules: a system <see cref="ITenantContext"/> (no tenant/user) and the system clock.
    /// Web hosts use AddPlatformWeb instead, which registers the HTTP-backed tenant context.
    /// </summary>
    public static IServiceCollection AddPlatformCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ITenantContext, SystemTenantContext>();
        return services;
    }

    public static IServiceCollection AddPlatformPersistence(this IServiceCollection services)
    {
        // Singleton: reads the current request's tenant/user/ip live from ITenantContext (also singleton,
        // backed by IHttpContextAccessor), so it is safe to share and resolvable when building DbContexts.
        services.TryAddSingleton<AuditInterceptor>();
        services.AddPipelineBehavior(typeof(ConcurrencyRetryBehavior<,>));
        return services;
    }

    /// <summary>
    /// Registers the read-replica factory for a module DbContext (no-tracking). The WRITE context is
    /// registered by <c>AddModuleDbContext</c> in the Messaging building block (it needs Wolverine's
    /// EF outbox integration). Call from the module's <c>IModule.RegisterServices</c>.
    /// </summary>
    public static IServiceCollection AddModuleReadDbContext<TContext>(
        this IServiceCollection services,
        string readConnectionString)
        where TContext : PlatformDbContext
    {
        services.AddSingleton<IReadDbContextFactory<TContext>>(sp =>
            new ReadDbContextFactory<TContext>(readConnectionString, sp.GetRequiredService<ITenantContext>()));
        return services;
    }
}
