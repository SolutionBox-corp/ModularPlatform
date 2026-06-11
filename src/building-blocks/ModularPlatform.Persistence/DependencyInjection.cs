using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence.Audit;
using ModularPlatform.Persistence.Behaviors;
using ModularPlatform.Persistence.Encryption;
using ModularPlatform.Persistence.Rls;

namespace ModularPlatform.Persistence;

public static class PersistenceServiceCollectionExtensions
{
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

    /// <summary>
    /// Registers the platform persistence pipeline: the audit/tenant/RLS/encryption interceptors and the command-only
    /// <see cref="ConcurrencyRetryBehavior{TRequest,TResponse}"/>. Invoked once PER MODULE (every module's
    /// RegisterServices calls it via AddModuleDbContext), so every registration here is idempotent — singletons via
    /// <c>TryAddSingleton</c>, the behavior via <c>TryAddEnumerable</c>/<c>TryAdd</c> — collapsing the per-module calls
    /// to a single set (NOT N nested retry layers).
    /// </summary>
    public static IServiceCollection AddPlatformPersistence(this IServiceCollection services)
    {
        // Singletons: they read the current request's tenant/user/ip live from ITenantContext (also singleton,
        // backed by IHttpContextAccessor), so they are safe to share and resolvable when building DbContexts.
        services.TryAddSingleton<AuditInterceptor>();
        services.TryAddSingleton<TenantStampingInterceptor>();
        services.TryAddSingleton<PrincipalSessionConnectionInterceptor>();
        services.TryAddSingleton<PersonalDataEncryptionInterceptor>();
        // Publishes the protector to the static accessor the cached-model decrypting converter reads from.
        // TryAddEnumerable dedups across the per-module AddPlatformPersistence calls; registered EARLY so it
        // starts before module seeders/backfills that query encrypted columns.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, PersonalDataEncryptionBootstrap>());
        services.AddOptions<RlsOptions>().BindConfiguration(RlsOptions.SectionName);
        services.AddOptions<AuditOptions>().BindConfiguration(AuditOptions.SectionName);
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
        {
            var rls = sp.GetRequiredService<IOptions<RlsOptions>>().Value;
            var runtimeConnectionString = RlsConnectionString.ForRuntime(readConnectionString, rls);
            return new ReadDbContextFactory<TContext>(
                runtimeConnectionString, sp.GetRequiredService<ITenantContext>(), rls.Enabled);
        });
        return services;
    }
}
